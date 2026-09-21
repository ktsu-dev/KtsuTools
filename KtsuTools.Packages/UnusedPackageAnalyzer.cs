// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Packages;

using System.Text.RegularExpressions;
using System.Xml.Linq;

/// <summary>
/// Statically decides which declared <c>PackageReference</c>s a project's own source
/// never refers to.
/// </summary>
/// <remarks>
/// The check is deliberately conservative: it reports a package only when nothing in the
/// project's source could plausibly be using it. Anything that cannot appear in source by
/// design — analyzers, MSBuild task packages, references whose compile assets are
/// suppressed — is classified <see cref="PackageUsage.BuildTimeOnly"/> rather than unused,
/// so the report stays free of the false positives that make such tools unusable.
/// </remarks>
internal static partial class UnusedPackageAnalyzer
{
	private const string IncludeAttribute = "Include";

	/// <summary>
	/// Identifier fragments that mark a package as build-time-only. Matched as a substring
	/// of the lowercased package id, so <c>StyleCop.Analyzers</c> and
	/// <c>Foo.SourceGenerators</c> are both caught.
	/// </summary>
	private static readonly string[] BuildTimeIdFragments =
	[
		"analyzer",
		"sourcegenerator",
		"source-generator",
		"codegenerator",
		"stylecop",
		"roslynator",
		"sonaranalyzer",
		"polyfill",
		"coverlet",
		"gitversion",
		"nerdbank.gitversioning",
		"sourcelink",
		"msbuild",
		"build.tasks",
		"buildtasks",
		"testadapter",
		"test.sdk",
		"runner.visualstudio",
		"runtime.native",
		"ilmerge",
		"ilrepack",
	];

	/// <summary>Asset kinds that let a package be referenced from C# source.</summary>
	private static readonly string[] CompileAssetKinds = ["all", "compile", "runtime"];

	/// <summary>
	/// Suffixes packages glue onto an identifier without a separating dot, leaving the id
	/// unable to match the namespace it ships — <c>LibreHardwareMonitorLib</c> against
	/// <c>LibreHardwareMonitor.Hardware</c>. Stripping one only adds a second candidate to
	/// match against; the real id is always tried first, so this can never lose a match.
	/// </summary>
	private static readonly string[] GluedIdSuffixes = ["library", "dotnet", "sharp", "lib", "net"];

	/// <summary>Shortest identifier still worth matching once a glued suffix is removed.</summary>
	private const int MinimumStrippedIdLength = 4;

	[GeneratedRegex(@"(?:^|\s)(?:global\s+)?using\s+(?:static\s+)?(?:[\w]+\s*=\s*)?([\w.]+)\s*;", RegexOptions.Multiline)]
	private static partial Regex UsingDirectiveRegex { get; }

	/// <summary>
	/// Scans every <c>.csproj</c> under <paramref name="rootPath"/> — or the single project
	/// file, if that is what it points at — and classifies each declared package reference.
	/// </summary>
	internal static UnusedPackageReport Analyze(string rootPath)
	{
		List<string> projectFiles = FindProjectFiles(rootPath);

		if (projectFiles.Count == 0)
		{
			return new UnusedPackageReport();
		}

		List<PackageFinding> findings = [];

		foreach (string projectFile in projectFiles)
		{
			findings.AddRange(AnalyzeProject(projectFile));
		}

		string? searchRoot = Directory.Exists(rootPath) ? rootPath : Path.GetDirectoryName(rootPath);

		return new UnusedPackageReport
		{
			Findings = findings,
			OrphanedPackageVersions = searchRoot is null ? [] : FindOrphanedPackageVersions(searchRoot, projectFiles),
			ProjectsScanned = projectFiles.Count,
		};
	}

	private static List<PackageFinding> AnalyzeProject(string projectFile)
	{
		List<PackageFinding> findings = [];
		XDocument? doc = TryLoad(projectFile);

		if (doc is null)
		{
			return findings;
		}

		string projectDirectory = Path.GetDirectoryName(projectFile) ?? ".";
		SourceIndex source = SourceIndex.Build(projectDirectory, doc);

		foreach (XElement packageRef in doc.Descendants("PackageReference"))
		{
			string? packageId = packageRef.Attribute(IncludeAttribute)?.Value;

			if (string.IsNullOrWhiteSpace(packageId))
			{
				continue;
			}

			string? buildTimeReason = ClassifyBuildTimeOnly(packageRef, packageId);

			if (buildTimeReason is not null)
			{
				findings.Add(new PackageFinding(projectFile, packageId, PackageUsage.BuildTimeOnly, buildTimeReason));
				continue;
			}

			findings.Add(source.References(packageId)
				? new PackageFinding(projectFile, packageId, PackageUsage.Used, "Referenced by project source")
				: new PackageFinding(projectFile, packageId, PackageUsage.Unused, "No source reference found"));
		}

		return findings;
	}

	/// <summary>
	/// Returns why the package can never appear in source, or <see langword="null"/> if it
	/// is an ordinary compile-time reference that source is expected to use.
	/// </summary>
	private static string? ClassifyBuildTimeOnly(XElement packageRef, string packageId)
	{
		if (ContainsAsset(Metadata(packageRef, "PrivateAssets"), "all"))
		{
			return "PrivateAssets=\"all\"";
		}

		string excludeAssets = Metadata(packageRef, "ExcludeAssets");

		if (ContainsAsset(excludeAssets, "all") || ContainsAsset(excludeAssets, "compile"))
		{
			return "Compile assets excluded";
		}

		string includeAssets = Metadata(packageRef, "IncludeAssets");

		if (!string.IsNullOrWhiteSpace(includeAssets) && !CompileAssetKinds.Any(kind => ContainsAsset(includeAssets, kind)))
		{
			return "IncludeAssets omits compile assets";
		}

		string lowered = packageId.ToLowerInvariant();

		return Array.Exists(BuildTimeIdFragments, fragment => lowered.Contains(fragment, StringComparison.Ordinal))
			? "Build-time-only package"
			: null;
	}

	/// <summary>Reads item metadata written either as an attribute or as a child element.</summary>
	private static string Metadata(XElement packageRef, string name) =>
		packageRef.Attribute(name)?.Value
		?? packageRef.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value
		?? string.Empty;

	private static bool ContainsAsset(string assets, string kind) =>
		assets.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Any(value => string.Equals(value, kind, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Finds <c>PackageVersion</c> entries under Central Package Management that no project
	/// and no directory-level props/targets file references.
	/// </summary>
	private static List<string> FindOrphanedPackageVersions(string searchRoot, List<string> projectFiles)
	{
		string propsPath = Path.Combine(searchRoot, "Directory.Packages.props");

		if (!File.Exists(propsPath))
		{
			return [];
		}

		XDocument? props = TryLoad(propsPath);

		if (props is null)
		{
			return [];
		}

		HashSet<string> referenced = new(StringComparer.OrdinalIgnoreCase);

		// Directory.Build.props/.targets add references to every project beneath them, so a
		// PackageVersion they consume is not orphaned even though no .csproj names it.
		List<string> referencingFiles = [.. projectFiles];
		referencingFiles.AddRange(Directory.EnumerateFiles(searchRoot, "Directory.Build.*", SearchOption.AllDirectories));

		foreach (string file in referencingFiles)
		{
			XDocument? doc = TryLoad(file);

			if (doc is null)
			{
				continue;
			}

			foreach (XElement element in doc.Descendants())
			{
				if (element.Name.LocalName is "PackageReference" or "GlobalPackageReference"
					&& element.Attribute(IncludeAttribute)?.Value is { Length: > 0 } id)
				{
					referenced.Add(id);
				}
			}
		}

		return [.. props.Descendants("PackageVersion")
			.Select(e => e.Attribute(IncludeAttribute)?.Value)
			.Where(id => !string.IsNullOrWhiteSpace(id) && !referenced.Contains(id!))
			.Select(id => id!)
			.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)];
	}

	private static XDocument? TryLoad(string path)
	{
		try
		{
			return XDocument.Parse(File.ReadAllText(path));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
		{
			return null;
		}
	}

	private static List<string> FindProjectFiles(string path)
	{
		if (File.Exists(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
		{
			return [path];
		}

		return Directory.Exists(path)
			? [.. Directory.EnumerateFiles(path, "*.csproj", SearchOption.AllDirectories)]
			: [];
	}

	/// <summary>
	/// The namespaces and raw text a single project's source refers to, built once per
	/// project so each package check is a set lookup rather than another disk read.
	/// </summary>
	private sealed class SourceIndex
	{
		private readonly HashSet<string> namespaces = new(StringComparer.OrdinalIgnoreCase);
		private readonly List<string> sourceText = [];

		internal static SourceIndex Build(string projectDirectory, XDocument project)
		{
			SourceIndex index = new();

			// MSBuild-declared implicit usings count as source references.
			foreach (XElement element in project.Descendants())
			{
				if (element.Name.LocalName == "Using" && element.Attribute(IncludeAttribute)?.Value is { Length: > 0 } ns)
				{
					index.namespaces.Add(ns);
				}
			}

			foreach (string file in EnumerateSourceFiles(projectDirectory))
			{
				string text;

				try
				{
					text = File.ReadAllText(file);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					continue;
				}

				index.sourceText.Add(text);

				foreach (Match match in UsingDirectiveRegex.Matches(text))
				{
					index.namespaces.Add(match.Groups[1].Value);
				}
			}

			return index;
		}

		private static IEnumerable<string> EnumerateSourceFiles(string projectDirectory) =>
			Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
				.Where(file => !IsGeneratedOutput(file, projectDirectory));

		private static bool IsGeneratedOutput(string file, string projectDirectory)
		{
			string relative = Path.GetRelativePath(projectDirectory, file);
			string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			return segments.Any(segment =>
				string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Decides whether the project's source refers to <paramref name="packageId"/>.
		/// </summary>
		/// <remarks>
		/// A package and the namespace it ships rarely match exactly, so a referenced
		/// namespace counts when it equals the package id, sits beneath it
		/// (<c>Foo.Bar</c> for package <c>Foo</c>), or is a parent of it
		/// (<c>Microsoft.Extensions.Logging</c> for package
		/// <c>Microsoft.Extensions.Logging.Abstractions</c>). Failing that, the raw source is
		/// checked for the id itself, which catches fully-qualified use and
		/// <c>$(Pkg...)</c>-style references.
		/// </remarks>
		internal bool References(string packageId)
		{
			foreach (string candidate in Candidates(packageId))
			{
				foreach (string ns in namespaces)
				{
					if (string.Equals(ns, candidate, StringComparison.OrdinalIgnoreCase)
						|| ns.StartsWith(candidate + ".", StringComparison.OrdinalIgnoreCase)
						|| candidate.StartsWith(ns + ".", StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
			}

			return sourceText.Any(text => text.Contains(packageId, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// The package id, followed by the id with a glued suffix removed when it carries one.
		/// </summary>
		private static IEnumerable<string> Candidates(string packageId)
		{
			yield return packageId;

			int lastDot = packageId.LastIndexOf('.');
			string lastSegment = packageId[(lastDot + 1)..];

			foreach (string suffix in GluedIdSuffixes)
			{
				if (lastSegment.Length - suffix.Length >= MinimumStrippedIdLength
					&& lastSegment.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
				{
					yield return packageId[..(packageId.Length - suffix.Length)];
					yield break;
				}
			}
		}
	}
}
