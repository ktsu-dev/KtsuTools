// Copyright (c) 2023-2026 ktsu-dev contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ktsu.KtsuTools.Test")]
// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Packages;

using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;
using ktsu.Semantics.Paths;
using KtsuTools.Core.Services.Process;
using Spectre.Console;

/// <summary>
/// Service for updating .NET packages and migrating to Central Package Management.
/// </summary>
public class PackagesService(IProcessService processService)
{
	private const string VersionAttribute = "Version";

	private readonly IProcessService processService = processService;
	private static readonly HttpClient SharedHttpClient = new();

	/// <summary>
	/// Updates NuGet packages in all .csproj files under the specified directory.
	/// </summary>
	public Task<int> UpdateAsync(AbsoluteDirectoryPath path, bool whatIf = false, bool includePrerelease = false, string source = "nuget", CancellationToken ct = default)
	{
		Ensure.NotNull(path);
		return UpdateInternalAsync(path.ToString(), whatIf, includePrerelease, source, ct);
	}

	/// <summary>
	/// Updates NuGet packages in a single .csproj file.
	/// </summary>
	public Task<int> UpdateAsync(AbsoluteFilePath path, bool whatIf = false, bool includePrerelease = false, string source = "nuget", CancellationToken ct = default)
	{
		Ensure.NotNull(path);
		return UpdateInternalAsync(path.ToString(), whatIf, includePrerelease, source, ct);
	}

	private async Task<int> UpdateInternalAsync(string fullPath, bool whatIf, bool includePrerelease, string source, CancellationToken ct)
	{
		_ = processService;

		if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
		{
			AnsiConsole.MarkupLine($"[red]Error: Path '{fullPath.EscapeMarkup()}' does not exist.[/]");
			return 1;
		}

		List<string> projectFiles = FindProjectFiles(fullPath);

		if (projectFiles.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No .csproj files found.[/]");
			return 0;
		}

		AnsiConsole.MarkupLine($"[blue]Found {projectFiles.Count} project file(s).[/]");

		if (whatIf)
		{
			AnsiConsole.MarkupLine("[yellow]Running in what-if mode. No changes will be made.[/]");
		}

		int updatedCount = 0;

		await AnsiConsole.Progress()
			.AutoClear(false)
			.HideCompleted(false)
			.StartAsync(async progressContext =>
			{
				ProgressTask task = progressContext.AddTask("[green]Updating packages[/]", maxValue: projectFiles.Count);

				foreach (string projectFile in projectFiles)
				{
					ct.ThrowIfCancellationRequested();
					string relativePath = Path.GetRelativePath(fullPath, projectFile);
					int updated = await UpdateProjectPackagesAsync(projectFile, relativePath, whatIf, includePrerelease, source, ct).ConfigureAwait(false);
					updatedCount += updated;
					task.Increment(1);
				}
			}).ConfigureAwait(false);

		AnsiConsole.MarkupLine($"[green]Done. {updatedCount} package(s) updated across {projectFiles.Count} project(s).[/]");
		return 0;
	}

	/// <summary>
	/// Reports <c>PackageReference</c>s that the declaring project's source never refers to.
	/// </summary>
	public Task<int> FindUnusedAsync(AbsoluteDirectoryPath path, bool showBuildTimeOnly = false, CancellationToken ct = default)
	{
		Ensure.NotNull(path);
		return FindUnusedInternalAsync(path.ToString(), showBuildTimeOnly, ct);
	}

	/// <summary>
	/// Reports <c>PackageReference</c>s that a single project's source never refers to.
	/// </summary>
	public Task<int> FindUnusedAsync(AbsoluteFilePath path, bool showBuildTimeOnly = false, CancellationToken ct = default)
	{
		Ensure.NotNull(path);
		return FindUnusedInternalAsync(path.ToString(), showBuildTimeOnly, ct);
	}

	private Task<int> FindUnusedInternalAsync(string fullPath, bool showBuildTimeOnly, CancellationToken ct)
	{
		_ = processService;
		ct.ThrowIfCancellationRequested();

		if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
		{
			AnsiConsole.MarkupLine($"[red]Error: Path '{fullPath.EscapeMarkup()}' does not exist.[/]");
			return Task.FromResult(1);
		}

		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(fullPath);

		if (report.ProjectsScanned == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No .csproj files found.[/]");
			return Task.FromResult(0);
		}

		RenderUnusedReport(report, fullPath, showBuildTimeOnly);
		return Task.FromResult(0);
	}

	private static void RenderUnusedReport(UnusedPackageReport report, string fullPath, bool showBuildTimeOnly)
	{
		AnsiConsole.MarkupLine($"[blue]Scanned {report.ProjectsScanned} project(s).[/]");

		List<PackageFinding> unused = [.. report.Unused];

		if (unused.Count == 0)
		{
			AnsiConsole.MarkupLine("[green]No unused package references found.[/]");
		}
		else
		{
			Table table = new();
			table.AddColumn("Project");
			table.AddColumn("Package");
			table.Border = TableBorder.Rounded;

			foreach (PackageFinding finding in unused.OrderBy(f => f.ProjectPath, StringComparer.OrdinalIgnoreCase)
				.ThenBy(f => f.PackageId, StringComparer.OrdinalIgnoreCase))
			{
				table.AddRow(DisplayPath(finding.ProjectPath, fullPath).EscapeMarkup(), finding.PackageId.EscapeMarkup());
			}

			AnsiConsole.Write(table);
			AnsiConsole.MarkupLine($"[yellow]{unused.Count} package reference(s) appear unused.[/]");
		}

		if (report.OrphanedPackageVersions.Count > 0)
		{
			AnsiConsole.MarkupLine($"[yellow]{report.OrphanedPackageVersions.Count} PackageVersion entry(s) in Directory.Packages.props are not referenced by any project:[/]");

			foreach (string packageId in report.OrphanedPackageVersions)
			{
				AnsiConsole.MarkupLine($"  {packageId.EscapeMarkup()}");
			}
		}

		if (showBuildTimeOnly)
		{
			RenderBuildTimeOnly(report, fullPath);
		}

		AnsiConsole.MarkupLine("[grey]Reported only. Removing a reference stays a human decision.[/]");
	}

	private static void RenderBuildTimeOnly(UnusedPackageReport report, string fullPath)
	{
		List<PackageFinding> buildTime = [.. report.BuildTimeOnly];

		if (buildTime.Count == 0)
		{
			return;
		}

		Table table = new();
		table.AddColumn("Project");
		table.AddColumn("Package");
		table.AddColumn("Held back because");
		table.Border = TableBorder.Rounded;

		foreach (PackageFinding finding in buildTime.OrderBy(f => f.ProjectPath, StringComparer.OrdinalIgnoreCase)
			.ThenBy(f => f.PackageId, StringComparer.OrdinalIgnoreCase))
		{
			table.AddRow(
				DisplayPath(finding.ProjectPath, fullPath).EscapeMarkup(),
				finding.PackageId.EscapeMarkup(),
				finding.Reason.EscapeMarkup());
		}

		AnsiConsole.Write(table);
	}

	private static string DisplayPath(string projectPath, string fullPath) =>
		Directory.Exists(fullPath) ? Path.GetRelativePath(fullPath, projectPath) : Path.GetFileName(projectPath);

	/// <summary>
	/// Migrates projects to Central Package Management.
	/// </summary>
	public async Task<int> MigrateToCpmAsync(AbsoluteDirectoryPath path, CancellationToken ct = default)
	{
		_ = processService;
		Ensure.NotNull(path);

		string fullPath = path.ToString();

		if (!Directory.Exists(fullPath))
		{
			AnsiConsole.MarkupLine($"[red]Error: Directory '{fullPath.EscapeMarkup()}' does not exist.[/]");
			return 1;
		}

		List<string> projectFiles = FindProjectFiles(fullPath);

		if (projectFiles.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No .csproj files found.[/]");
			return 0;
		}

		string propsPath = Path.Combine(fullPath, "Directory.Packages.props");

		if (File.Exists(propsPath))
		{
			AnsiConsole.MarkupLine("[yellow]Directory.Packages.props already exists. Merging.[/]");
		}

		AnsiConsole.MarkupLine($"[blue]Migrating {projectFiles.Count} project(s) to CPM...[/]");

		Dictionary<string, string> allPackages = [];

		await AnsiConsole.Progress()
			.AutoClear(false)
			.HideCompleted(false)
			.StartAsync(async progressContext =>
			{
				ProgressTask collectTask = progressContext.AddTask("[green]Collecting package references[/]", maxValue: projectFiles.Count);

				foreach (string projectFile in projectFiles)
				{
					ct.ThrowIfCancellationRequested();
					Dictionary<string, string> packages = await GetPackageReferencesAsync(projectFile, ct).ConfigureAwait(false);

					foreach ((string packageName, string version) in packages)
					{
						if (!allPackages.TryGetValue(packageName, out string? existingVersion) || CompareVersions(version, existingVersion) > 0)
						{
							allPackages[packageName] = version;
						}
					}

					collectTask.Increment(1);
				}

				ProgressTask createTask = progressContext.AddTask("[green]Writing Directory.Packages.props[/]", maxValue: 1);
				if (File.Exists(propsPath))
				{
					await MergePackagesPropsAsync(propsPath, allPackages, ct).ConfigureAwait(false);
				}
				else
				{
					await CreatePackagesPropsAsync(propsPath, allPackages, ct).ConfigureAwait(false);
				}

				createTask.Increment(1);

				ProgressTask removeTask = progressContext.AddTask("[green]Removing versions from project files[/]", maxValue: projectFiles.Count);

				foreach (string projectFile in projectFiles)
				{
					ct.ThrowIfCancellationRequested();
					await RemoveVersionsFromProjectAsync(projectFile, ct).ConfigureAwait(false);
					removeTask.Increment(1);
				}
			}).ConfigureAwait(false);

		AnsiConsole.MarkupLine($"[green]Done. Migrated {allPackages.Count} package(s) to CPM.[/]");

		Table table = new();
		table.AddColumn("Package");
		table.AddColumn(VersionAttribute);
		table.Border = TableBorder.Rounded;

		foreach ((string packageName, string version) in allPackages.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
		{
			table.AddRow(packageName.EscapeMarkup(), version.EscapeMarkup());
		}

		AnsiConsole.Write(table);
		return 0;
	}

	private static async Task<int> UpdateProjectPackagesAsync(string projectFile, string relativePath, bool whatIf, bool includePrerelease, string source, CancellationToken ct)
	{
		Dictionary<string, string> packages = await GetPackageReferencesAsync(projectFile, ct).ConfigureAwait(false);

		if (packages.Count == 0)
		{
			return 0;
		}

		int updatedCount = 0;

		foreach ((string packageName, string currentVersion) in packages)
		{
			ct.ThrowIfCancellationRequested();

			string? latestVersion = await GetLatestVersionAsync(packageName, includePrerelease, source, ct).ConfigureAwait(false);

			if (latestVersion is null || latestVersion == currentVersion)
			{
				continue;
			}

			if (CompareVersions(latestVersion, currentVersion) <= 0)
			{
				continue;
			}

			AnsiConsole.MarkupLine($"  [blue]{relativePath.EscapeMarkup()}[/]: {packageName.EscapeMarkup()} [red]{currentVersion.EscapeMarkup()}[/] -> [green]{latestVersion.EscapeMarkup()}[/]");

			if (!whatIf)
			{
				await UpdatePackageVersionInFileAsync(projectFile, packageName, latestVersion, ct).ConfigureAwait(false);
			}

			updatedCount++;
		}

		return updatedCount;
	}

	private static async Task<Dictionary<string, string>> GetPackageReferencesAsync(string projectFile, CancellationToken ct)
	{
		Dictionary<string, string> packages = [];

		try
		{
			string content = await File.ReadAllTextAsync(projectFile, ct).ConfigureAwait(false);
			XDocument doc = XDocument.Parse(content);
			IEnumerable<XElement> packageRefs = doc.Descendants("PackageReference");

			foreach (XElement packageRef in packageRefs)
			{
				string? name = packageRef.Attribute("Include")?.Value;
				string? version = packageRef.Attribute(VersionAttribute)?.Value;

				if (name is not null && version is not null)
				{
					packages[name] = version;
				}
			}
		}
		catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
		{
			AnsiConsole.MarkupLine($"[yellow]Warning: Could not read {Path.GetFileName(projectFile).EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
		}

		return packages;
	}

	private static async Task UpdatePackageVersionInFileAsync(string projectFile, string packageName, string newVersion, CancellationToken ct)
	{
		try
		{
			string content = await File.ReadAllTextAsync(projectFile, ct).ConfigureAwait(false);
			XDocument doc = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
			IEnumerable<XElement> packageRefs = doc.Descendants("PackageReference");

			foreach (XElement packageRef in packageRefs)
			{
				string? name = packageRef.Attribute("Include")?.Value;
				if (string.Equals(name, packageName, StringComparison.OrdinalIgnoreCase))
				{
					packageRef.SetAttributeValue(VersionAttribute, newVersion);
				}
			}

			await File.WriteAllTextAsync(projectFile, doc.ToString(), ct).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
		{
			AnsiConsole.MarkupLine($"[red]Error updating {Path.GetFileName(projectFile).EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
		}
	}

	private static async Task<string?> GetLatestVersionAsync(string packageName, bool includePrerelease, string source, CancellationToken ct)
	{
		if (!string.Equals(source, "nuget", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		try
		{
			string url = $"https://api.nuget.org/v3-flatcontainer/{packageName.ToLowerInvariant()}/index.json";
			HttpResponseMessage response = await SharedHttpClient.GetAsync(new Uri(url), ct).ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
			using JsonDocument doc = JsonDocument.Parse(json);

			if (!doc.RootElement.TryGetProperty("versions", out JsonElement versionsElement))
			{
				return null;
			}

			string? latestVersion = null;

			foreach (JsonElement versionElement in versionsElement.EnumerateArray())
			{
				string? version = versionElement.GetString();
				if (version is null)
				{
					continue;
				}

				bool isPrerelease = version.Contains('-', StringComparison.Ordinal);
				if (!includePrerelease && isPrerelease)
				{
					continue;
				}

				latestVersion = version;
			}

			return latestVersion;
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
		{
			return null;
		}
	}

	private static async Task CreatePackagesPropsAsync(string propsPath, Dictionary<string, string> packages, CancellationToken ct)
	{
		XDocument doc = new(
			new XElement("Project",
				new XElement("PropertyGroup",
					new XElement("ManagePackageVersionsCentrally", "true")),
				new XElement("ItemGroup",
					packages.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
						.Select(kvp => new XElement("PackageVersion",
							new XAttribute("Include", kvp.Key),
							new XAttribute(VersionAttribute, kvp.Value))))));

		await File.WriteAllTextAsync(propsPath, doc.ToString(), ct).ConfigureAwait(false);
	}

	/// <summary>
	/// Adds the collected versions to an existing Directory.Packages.props without disturbing anything
	/// already in it.
	/// </summary>
	/// <remarks>
	/// An entry that is already managed centrally keeps its version unless a project asked for a higher
	/// one. Everything else in the file, such as comments, properties, GlobalPackageReference items and
	/// conditional ItemGroups, is left exactly as it was. New entries go into the first unconditional
	/// ItemGroup that already holds PackageVersion items, or into a new ItemGroup when there is none.
	/// </remarks>
	private static async Task MergePackagesPropsAsync(string propsPath, Dictionary<string, string> packages, CancellationToken ct)
	{
		string content = await File.ReadAllTextAsync(propsPath, ct).ConfigureAwait(false);
		XDocument doc = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
		XElement project = doc.Root ?? throw new InvalidDataException($"{propsPath} has no root element.");
		XNamespace ns = project.Name.Namespace;

		List<XElement> existing = [.. project.Descendants(ns + "PackageVersion")];
		XElement? itemGroup = existing
			.Select(e => e.Parent)
			.FirstOrDefault(parent => parent is not null && parent.Name == ns + "ItemGroup" && parent.Attribute("Condition") is null);

		foreach ((string packageName, string version) in packages.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
		{
			List<XElement> matches = [.. existing.Where(e => string.Equals(e.Attribute("Include")?.Value, packageName, StringComparison.OrdinalIgnoreCase))];

			if (matches.Count > 0)
			{
				foreach (XElement match in matches)
				{
					string? currentVersion = match.Attribute(VersionAttribute)?.Value;
					if (currentVersion is not null && CompareVersions(version, currentVersion) > 0)
					{
						match.SetAttributeValue(VersionAttribute, version);
					}
				}

				continue;
			}

			XElement entry = new(ns + "PackageVersion",
				new XAttribute("Include", packageName),
				new XAttribute(VersionAttribute, version));

			if (itemGroup is null)
			{
				itemGroup = new XElement(ns + "ItemGroup");
				AppendIndented(project, itemGroup);
			}

			AppendIndented(itemGroup, entry);
			existing.Add(entry);
		}

		// Parsing normalizes line endings to \n, so put back the ones the file was written with.
		string declaration = doc.Declaration is null ? string.Empty : doc.Declaration + "\n";
		string merged = declaration + doc.ToString(SaveOptions.DisableFormatting);
		if (content.Contains("\r\n", StringComparison.Ordinal))
		{
			merged = merged.ReplaceLineEndings("\r\n");
		}

		await File.WriteAllTextAsync(propsPath, merged, ct).ConfigureAwait(false);
	}

	/// <summary>
	/// Appends an element on its own line, copying the indentation already used around the parent so
	/// a whitespace-preserving document stays readable.
	/// </summary>
	private static void AppendIndented(XElement parent, XElement child)
	{
		string parentIndent = parent.PreviousNode is XText { Value: string before } && string.IsNullOrWhiteSpace(before)
			? before
			: "\n";
		XElement? lastSibling = parent.Elements().LastOrDefault();

		if (lastSibling is not null)
		{
			string separator = lastSibling.PreviousNode is XText { Value: string text } && string.IsNullOrWhiteSpace(text)
				? text
				: parentIndent + "  ";
			lastSibling.AddAfterSelf(new XText(separator), child);
			return;
		}

		if (parent.LastNode is XText { Value: string closing } trailing && string.IsNullOrWhiteSpace(closing))
		{
			trailing.Remove();
		}

		parent.Add(new XText(parentIndent + "  "), child, new XText(parentIndent));
	}

	private static async Task RemoveVersionsFromProjectAsync(string projectFile, CancellationToken ct)
	{
		try
		{
			string content = await File.ReadAllTextAsync(projectFile, ct).ConfigureAwait(false);
			XDocument doc = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
			IEnumerable<XElement> packageRefs = doc.Descendants("PackageReference");

			foreach (XElement packageRef in packageRefs)
			{
				packageRef.Attribute(VersionAttribute)?.Remove();
			}

			await File.WriteAllTextAsync(projectFile, doc.ToString(), ct).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
		{
			AnsiConsole.MarkupLine($"[yellow]Warning: Could not update {Path.GetFileName(projectFile).EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
		}
	}

	private static List<string> FindProjectFiles(string path)
	{
		if (File.Exists(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
		{
			return [path];
		}

		if (Directory.Exists(path))
		{
			return [.. Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories)];
		}

		return [];
	}

	private static int CompareVersions(string version1, string version2)
	{
		string v1Clean = version1.Split('-')[0];
		string v2Clean = version2.Split('-')[0];
		string[] parts1 = v1Clean.Split('.');
		string[] parts2 = v2Clean.Split('.');
		int maxParts = Math.Max(parts1.Length, parts2.Length);

		for (int i = 0; i < maxParts; i++)
		{
			int p1 = i < parts1.Length && int.TryParse(parts1[i], out int v1) ? v1 : 0;
			int p2 = i < parts2.Length && int.TryParse(parts2[i], out int v2) ? v2 : 0;

			if (p1 != p2)
			{
				return p1.CompareTo(p2);
			}
		}

		return 0;
	}
}
