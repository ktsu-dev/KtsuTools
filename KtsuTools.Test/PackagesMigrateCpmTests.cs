// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Xml.Linq;
using ktsu.Semantics.Paths;
using KtsuTools.Core.Services.Process;
using KtsuTools.Packages;
using Moq;

/// <summary>
/// Covers <c>packages migrate-cpm</c> against a tree that already has a Directory.Packages.props,
/// which is the partly migrated solution the verb is most likely to be run on.
/// </summary>
[TestClass]
public class PackagesMigrateCpmTests
{
	private string root = string.Empty;

	[TestInitialize]
	public void CreateWorkspace()
	{
		root = Path.Combine(Path.GetTempPath(), "ktsu-packages-cpm-" + Guid.NewGuid().ToString("N"));
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

	private string PropsPath => Path.Combine(root, "Directory.Packages.props");

	private static PackagesService BuildService() => new(Mock.Of<IProcessService>());

	private AbsoluteDirectoryPath RootPath => AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

	private void WriteProject(string name, string itemGroupBody)
	{
		string projectDirectory = Path.Combine(root, name);
		Directory.CreateDirectory(projectDirectory);
		File.WriteAllText(Path.Combine(projectDirectory, name + ".csproj"), $"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <ItemGroup>
			{itemGroupBody}
			  </ItemGroup>
			</Project>
			""");
	}

	private Dictionary<string, string?> ReadPackageVersions() =>
		XDocument.Load(PropsPath)
			.Descendants("PackageVersion")
			.ToDictionary(e => e.Attribute("Include")!.Value, e => e.Attribute("Version")?.Value, StringComparer.OrdinalIgnoreCase);

	[TestMethod]
	public async Task KeepsExistingEntriesAndUnrelatedContentWhileAddingNewOnes()
	{
		await File.WriteAllTextAsync(PropsPath, """
			<Project>
			  <!-- Pinned for the analyzers. -->
			  <PropertyGroup>
			    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
			    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageVersion Include="Polly" Version="8.0.0" />
			  </ItemGroup>
			  <ItemGroup Condition="'$(TargetFramework)' == 'net472'">
			    <PackageVersion Include="System.Memory" Version="4.5.5" />
			  </ItemGroup>
			  <ItemGroup>
			    <GlobalPackageReference Include="Nerdbank.GitVersioning" Version="3.6.0" />
			  </ItemGroup>
			</Project>
			""").ConfigureAwait(false);
		WriteProject("A", """    <PackageReference Include="Polly" />""");
		WriteProject("B", """    <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />""");

		int exitCode = await BuildService().MigrateToCpmAsync(RootPath).ConfigureAwait(false);

		Assert.AreEqual(0, exitCode);
		Dictionary<string, string?> versions = ReadPackageVersions();
		Assert.AreEqual("8.0.0", versions["Polly"]);
		Assert.AreEqual("4.5.5", versions["System.Memory"]);
		Assert.AreEqual("13.0.1", versions["Newtonsoft.Json"]);

		// The raw strings above carry whatever line endings the checkout gave this file, and the merge
		// keeps the props file's own, so compare layout independently of them.
		string props = (await File.ReadAllTextAsync(PropsPath).ConfigureAwait(false)).ReplaceLineEndings("\n");
		StringAssert.Contains(props, "<!-- Pinned for the analyzers. -->");
		StringAssert.Contains(props, "<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>");
		StringAssert.Contains(props, "Condition=\"'$(TargetFramework)' == 'net472'\"");
		StringAssert.Contains(props, "<GlobalPackageReference Include=\"Nerdbank.GitVersioning\" Version=\"3.6.0\" />");
		StringAssert.Contains(props, "<PackageVersion Include=\"Polly\" Version=\"8.0.0\" />\n    <PackageVersion Include=\"Newtonsoft.Json\" Version=\"13.0.1\" />\n  </ItemGroup>");

		XElement newEntry = XDocument.Load(PropsPath).Descendants("PackageVersion")
			.Single(e => e.Attribute("Include")!.Value == "Newtonsoft.Json");
		Assert.IsNull(newEntry.Parent!.Attribute("Condition"), "A new entry must not land in a conditional ItemGroup.");
	}

	[TestMethod]
	public async Task RaisesAnExistingEntryOnlyWhenAProjectAsksForAHigherVersion()
	{
		await File.WriteAllTextAsync(PropsPath, """
			<Project>
			  <ItemGroup>
			    <PackageVersion Include="Polly" Version="8.0.0" />
			    <PackageVersion Include="Serilog" Version="4.0.0" />
			  </ItemGroup>
			</Project>
			""").ConfigureAwait(false);
		WriteProject("A", """    <PackageReference Include="polly" Version="8.2.0" />""");
		WriteProject("B", """    <PackageReference Include="Serilog" Version="3.1.1" />""");

		int exitCode = await BuildService().MigrateToCpmAsync(RootPath).ConfigureAwait(false);

		Assert.AreEqual(0, exitCode);
		Dictionary<string, string?> versions = ReadPackageVersions();
		Assert.AreEqual(2, versions.Count, "A reference that differs only in case must update the existing entry, not add a second one.");
		Assert.AreEqual("8.2.0", versions["Polly"]);
		Assert.AreEqual("4.0.0", versions["Serilog"]);
	}

	[TestMethod]
	public async Task AddsAnItemGroupWhenTheExistingFileHasNone()
	{
		await File.WriteAllTextAsync(PropsPath, """
			<Project>
			  <PropertyGroup>
			    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
			  </PropertyGroup>
			</Project>
			""").ConfigureAwait(false);
		WriteProject("A", """    <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />""");

		int exitCode = await BuildService().MigrateToCpmAsync(RootPath).ConfigureAwait(false);

		Assert.AreEqual(0, exitCode);
		Assert.AreEqual("13.0.1", ReadPackageVersions()["Newtonsoft.Json"]);
		// The raw strings above carry whatever line endings the checkout gave this file, and the merge
		// keeps the props file's own, so compare layout independently of them.
		string props = (await File.ReadAllTextAsync(PropsPath).ConfigureAwait(false)).ReplaceLineEndings("\n");
		StringAssert.Contains(props, "<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>");
		StringAssert.Contains(props, "  </PropertyGroup>\n  <ItemGroup>\n    <PackageVersion Include=\"Newtonsoft.Json\" Version=\"13.0.1\" />\n  </ItemGroup>\n</Project>");
	}

	[TestMethod]
	public async Task CreatesThePropsFileWhenNoneExists()
	{
		WriteProject("A", """    <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />""");

		int exitCode = await BuildService().MigrateToCpmAsync(RootPath).ConfigureAwait(false);

		Assert.AreEqual(0, exitCode);
		Assert.AreEqual("13.0.1", ReadPackageVersions()["Newtonsoft.Json"]);
		StringAssert.Contains(await File.ReadAllTextAsync(PropsPath).ConfigureAwait(false), "<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>");
	}
}
