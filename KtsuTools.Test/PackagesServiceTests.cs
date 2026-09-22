// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using ktsu.Semantics.Paths;
using KtsuTools.Core.Services.Process;
using KtsuTools.Packages;
using Moq;

/// <summary>
/// Covers what <c>packages find-unused</c> actually prints, which is the whole deliverable of a
/// report-only verb.
/// </summary>
[TestClass]
public class PackagesServiceTests
{
	private string root = string.Empty;

	[TestInitialize]
	public void CreateWorkspace()
	{
		root = Path.Combine(Path.GetTempPath(), "ktsu-packages-svc-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
	}

	[TestCleanup]
	public void RemoveWorkspace()
	{
		if (Directory.Exists(root))
		{
			Directory.Delete(root, recursive: true);
		}
	}

	private static PackagesService BuildService() => new(Mock.Of<IProcessService>());

	private AbsoluteDirectoryPath RootPath => AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

	private string WriteProject(string name, string itemGroupBody, string? source = null)
	{
		string projectDirectory = Path.Combine(root, name);
		Directory.CreateDirectory(projectDirectory);

		string projectPath = Path.Combine(projectDirectory, name + ".csproj");
		File.WriteAllText(projectPath, $"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <ItemGroup>
			{itemGroupBody}
			  </ItemGroup>
			</Project>
			""");

		File.WriteAllText(
			Path.Combine(projectDirectory, "Program.cs"),
			source ?? $"namespace {name}; internal static class Program {{ }}");

		return projectPath;
	}

	[TestMethod]
	public async Task NamesTheUnusedReferenceAndItsProject()
	{
		WriteProject("Sample", """    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />""");
		PackagesService service = BuildService();

		string output = await ConsoleCapture.CaptureAsync(() => service.FindUnusedAsync(RootPath)).ConfigureAwait(false);

		StringAssert.Contains(output, "Scanned 1 project(s).", StringComparison.Ordinal);
		StringAssert.Contains(output, "Newtonsoft.Json", StringComparison.Ordinal);
		StringAssert.Contains(output, "Sample.csproj", StringComparison.Ordinal);
		StringAssert.Contains(output, "1 package reference(s) appear unused.", StringComparison.Ordinal);
		StringAssert.Contains(output, "Removing a reference stays a human decision.", StringComparison.Ordinal);
	}

	[TestMethod]
	public async Task SaysSoWhenEveryReferenceIsUsed()
	{
		WriteProject(
			"Sample",
			"""    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />""",
			"using Newtonsoft.Json;\n");
		PackagesService service = BuildService();

		string output = await ConsoleCapture.CaptureAsync(() => service.FindUnusedAsync(RootPath)).ConfigureAwait(false);

		StringAssert.Contains(output, "No unused package references found.", StringComparison.Ordinal);
		Assert.IsFalse(output.Contains("appear unused", StringComparison.Ordinal));
	}

	[TestMethod]
	public async Task ReportsNoProjectsWhenTheTreeHasNone()
	{
		PackagesService service = BuildService();

		string output = await ConsoleCapture.CaptureAsync(() => service.FindUnusedAsync(RootPath)).ConfigureAwait(false);

		StringAssert.Contains(output, "No .csproj files found.", StringComparison.Ordinal);
	}

	[TestMethod]
	public async Task FailsWhenThePathDoesNotExist()
	{
		PackagesService service = BuildService();
		AbsoluteDirectoryPath missing = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.Combine(root, "nope"));

		int exitCode = 0;
		string output = await ConsoleCapture.CaptureAsync(async () =>
			exitCode = await service.FindUnusedAsync(missing).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exitCode);
		StringAssert.Contains(output, "does not exist", StringComparison.Ordinal);
	}

	[TestMethod]
	public async Task ScansASingleProjectFileWhenGivenOne()
	{
		string projectPath = WriteProject("Sample", """    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />""");
		PackagesService service = BuildService();
		AbsoluteFilePath file = AbsoluteFilePath.Create<AbsoluteFilePath>(projectPath);

		string output = await ConsoleCapture.CaptureAsync(() => service.FindUnusedAsync(file)).ConfigureAwait(false);

		StringAssert.Contains(output, "Scanned 1 project(s).", StringComparison.Ordinal);
		// A single project is named by filename, since there is no directory to be relative to.
		StringAssert.Contains(output, "Sample.csproj", StringComparison.Ordinal);
		StringAssert.Contains(output, "Newtonsoft.Json", StringComparison.Ordinal);
	}

	[TestMethod]
	public async Task HidesBuildTimeOnlyReferencesUntilAskedForThem()
	{
		WriteProject("Sample", """    <PackageReference Include="StyleCop.Analyzers" Version="1.2.0" />""");
		PackagesService service = BuildService();

		string quiet = await ConsoleCapture.CaptureAsync(() => service.FindUnusedAsync(RootPath)).ConfigureAwait(false);
		Assert.IsFalse(quiet.Contains("StyleCop.Analyzers", StringComparison.Ordinal));
		StringAssert.Contains(quiet, "No unused package references found.", StringComparison.Ordinal);

		string verbose = await ConsoleCapture.CaptureAsync(
			() => service.FindUnusedAsync(RootPath, showBuildTimeOnly: true)).ConfigureAwait(false);
		StringAssert.Contains(verbose, "StyleCop.Analyzers", StringComparison.Ordinal);
		StringAssert.Contains(verbose, "Build-time-only package", StringComparison.Ordinal);
	}

	[TestMethod]
	public async Task ListsOrphanedPackageVersions()
	{
		WriteProject("Sample", """    <PackageReference Include="Newtonsoft.Json" />""", "using Newtonsoft.Json;\n");
		await File.WriteAllTextAsync(Path.Combine(root, "Directory.Packages.props"), """
			<Project>
			  <ItemGroup>
			    <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
			    <PackageVersion Include="Nobody.Uses.This" Version="1.0.0" />
			  </ItemGroup>
			</Project>
			""").ConfigureAwait(false);
		PackagesService service = BuildService();

		string output = await ConsoleCapture.CaptureAsync(() => service.FindUnusedAsync(RootPath)).ConfigureAwait(false);

		StringAssert.Contains(output, "1 PackageVersion entry(s)", StringComparison.Ordinal);
		StringAssert.Contains(output, "Nobody.Uses.This", StringComparison.Ordinal);
	}

	[TestMethod]
	public async Task SucceedsWithoutFindings()
	{
		WriteProject("Sample", """    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />""", "using Newtonsoft.Json;\n");
		PackagesService service = BuildService();

		int exitCode = 0;
		await ConsoleCapture.CaptureAsync(async () =>
			exitCode = await service.FindUnusedAsync(RootPath).ConfigureAwait(false)).ConfigureAwait(false);

		// Report-only: findings are information, not a failed run.
		Assert.AreEqual(0, exitCode);
	}
}
