// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Xml.Linq;
using ktsu.Semantics.Paths;
using KtsuTools.Core.Services.Process;
using KtsuTools.Packages;
using Moq;

/// <summary>
/// Covers how <c>packages update</c> decides whether a declared version can be bumped, and what it
/// writes back to the project file when it does.
/// </summary>
[TestClass]
public class PackagesUpdateTests
{
	private string root = string.Empty;

	[TestInitialize]
	public void CreateWorkspace()
	{
		root = Path.Join(Path.GetTempPath(), "ktsu-packages-update-" + Guid.NewGuid().ToString("N"));
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

	[TestMethod]
	[DataRow("$(NjVer)")]
	[DataRow("[3.0.0,4.0.0)")]
	[DataRow("4.*")]
	public void IsUpgradeRejectsVersionsThatAreNotConcrete(string declared)
	{
		Assert.IsFalse(PackagesService.IsUpgrade(declared, "99.0.0"), $"'{declared}' is a deliberate declaration, not a pin to bump.");
	}

	[TestMethod]
	public void IsUpgradeMovesAPrereleaseToItsStableRelease()
	{
		Assert.IsTrue(PackagesService.IsUpgrade("1.0.0-beta", "1.0.0"));
		Assert.IsFalse(PackagesService.IsUpgrade("1.0.0", "1.0.0-beta"));
		Assert.IsFalse(PackagesService.IsUpgrade("1.0.0", "1.0.0"));
	}

	[TestMethod]
	public void CompareVersionsOrdersPrereleasesBelowTheirRelease()
	{
		Assert.IsTrue(PackagesService.CompareVersions("1.0.0", "1.0.0-beta") > 0);
		Assert.IsTrue(PackagesService.CompareVersions("1.0.0-beta.2", "1.0.0-beta.10") < 0);
		Assert.IsTrue(PackagesService.CompareVersions("2.0.0", "10.0.0") < 0);
	}

	[TestMethod]
	public async Task UpdateLeavesNonConcreteVersionsAndTheDeclarationAlone()
	{
		string original = string.Join("\r\n",
			"<?xml version=\"1.0\" encoding=\"utf-8\"?>",
			"<Project Sdk=\"Microsoft.NET.Sdk\">",
			"  <ItemGroup>",
			"    <PackageReference Include=\"Property.Pkg\" Version=\"$(NjVer)\" />",
			"    <PackageReference Include=\"Range.Pkg\" Version=\"[3.0.0,4.0.0)\" />",
			"    <PackageReference Include=\"Float.Pkg\" Version=\"4.*\" />",
			"    <PackageReference Include=\"Pinned.Pkg\" Version=\"12.0.1\" />",
			"  </ItemGroup>",
			"</Project>",
			string.Empty);
		string projectPath = Path.Join(root, "Sample.csproj");
		await File.WriteAllTextAsync(projectPath, original).ConfigureAwait(false);

		foreach (string package in new[] { "Property.Pkg", "Range.Pkg", "Float.Pkg", "Pinned.Pkg" })
		{
			await PackagesService.UpdatePackageVersionInFileAsync(projectPath, package, "13.0.4", CancellationToken.None).ConfigureAwait(false);
		}

		string updated = await File.ReadAllTextAsync(projectPath).ConfigureAwait(false);
		Assert.AreEqual(original.Replace("Version=\"12.0.1\"", "Version=\"13.0.4\"", StringComparison.Ordinal), updated);
	}

	[TestMethod]
	public void CompareVersionsSortsNonConcreteVersionsBelowConcreteOnes()
	{
		Assert.IsTrue(PackagesService.CompareVersions("1.0.0", "$(NjVer)") > 0);
		Assert.IsTrue(PackagesService.CompareVersions("4.*", "0.0.1") < 0);
		Assert.AreEqual(0, PackagesService.CompareVersions("$(A)", "[1.0,2.0)"));
	}

	[TestMethod]
	public async Task UpdateReportsNonConcreteVersionsAsSkippedAndLeavesThemAlone()
	{
		string original = string.Join("\n",
			"<Project Sdk=\"Microsoft.NET.Sdk\">",
			"  <ItemGroup>",
			"    <PackageReference Include=\"Property.Pkg\" Version=\"$(NjVer)\" />",
			"    <PackageReference Include=\"Float.Pkg\" Version=\"4.*\" />",
			"  </ItemGroup>",
			"</Project>",
			string.Empty);
		string projectPath = Path.Join(root, "Sample.csproj");
		await File.WriteAllTextAsync(projectPath, original).ConfigureAwait(false);

		PackagesService service = new(Mock.Of<IProcessService>());
		int exitCode = -1;
		string output = await ConsoleCapture.CaptureAsync(async () =>
			exitCode = await service.UpdateAsync(AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root)).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(0, exitCode);
		StringAssert.Contains(output, "Property.Pkg $(NjVer) skipped", StringComparison.Ordinal);
		StringAssert.Contains(output, "Float.Pkg 4.* skipped", StringComparison.Ordinal);
		StringAssert.Contains(output, "0 package(s) updated", StringComparison.Ordinal);
		Assert.AreEqual(original, await File.ReadAllTextAsync(projectPath).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task MigrateToCpmKeepsACentralVersionThatIsAProperty()
	{
		string propsPath = Path.Join(root, "Directory.Packages.props");
		await File.WriteAllTextAsync(propsPath, string.Join("\n",
			"<?xml version=\"1.0\" encoding=\"utf-8\"?>",
			"<Project>",
			"  <ItemGroup>",
			"    <PackageVersion Include=\"Central.Pkg\" Version=\"$(CentralVer)\" />",
			"  </ItemGroup>",
			"</Project>",
			string.Empty)).ConfigureAwait(false);

		string projectDirectory = Path.Join(root, "App");
		Directory.CreateDirectory(projectDirectory);
		await File.WriteAllTextAsync(Path.Join(projectDirectory, "App.csproj"), string.Join("\n",
			"<Project Sdk=\"Microsoft.NET.Sdk\">",
			"  <ItemGroup>",
			"    <PackageReference Include=\"Central.Pkg\" Version=\"2.0.0\" />",
			"  </ItemGroup>",
			"</Project>",
			string.Empty)).ConfigureAwait(false);

		PackagesService service = new(Mock.Of<IProcessService>());
		await ConsoleCapture.CaptureAsync(() => service.MigrateToCpmAsync(AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root))).ConfigureAwait(false);

		string props = await File.ReadAllTextAsync(propsPath).ConfigureAwait(false);
		Assert.AreEqual(
			"$(CentralVer)",
			XDocument.Parse(props).Descendants("PackageVersion").Single().Attribute("Version")?.Value,
			"A concrete project version must not overwrite a central property indirection.");
		Assert.IsTrue(props.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Project>", StringComparison.Ordinal), "The declaration is kept, with no blank line added after it.");
	}
}
