// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using KtsuTools.Packages;

[TestClass]
public class UnusedPackageAnalyzerTests
{
	private string root = string.Empty;

	[TestInitialize]
	public void CreateWorkspace()
	{
		root = Path.Combine(Path.GetTempPath(), "ktsu-find-unused-" + Guid.NewGuid().ToString("N"));
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

	private string WriteProject(string name, string itemGroupBody, params (string FileName, string Contents)[] sources)
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

		foreach ((string fileName, string contents) in sources)
		{
			File.WriteAllText(Path.Combine(projectDirectory, fileName), contents);
		}

		return projectPath;
	}

	private static PackageFinding Finding(UnusedPackageReport report, string packageId) =>
		report.Findings.Single(f => f.PackageId == packageId);

	[TestMethod]
	public void ReportsAReferenceNoSourceUses()
	{
		WriteProject(
			"Sample",
			"""    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />""",
			("Program.cs", "namespace Sample; internal static class Program { }"));

		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(root);

		Assert.AreEqual(1, report.ProjectsScanned);
		Assert.AreEqual(PackageUsage.Unused, Finding(report, "Newtonsoft.Json").Usage);
		Assert.AreEqual("Newtonsoft.Json", report.Unused.Single().PackageId);
	}

	[TestMethod]
	public void DoesNotReportAReferenceASourceUsingDirectiveNames()
	{
		WriteProject(
			"Sample",
			"""    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />""",
			("Program.cs", "using Newtonsoft.Json;\n\nnamespace Sample; internal static class Program { }"));

		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(root);

		Assert.AreEqual(PackageUsage.Used, Finding(report, "Newtonsoft.Json").Usage);
		Assert.IsFalse(report.Unused.Any());
	}

	[TestMethod]
	public void TreatsANamespaceBeneathThePackageIdAsUse()
	{
		WriteProject(
			"Sample",
			"""    <PackageReference Include="Spectre.Console" Version="0.57.0" />""",
			("Program.cs", "using Spectre.Console.Rendering;\n\nnamespace Sample; internal static class Program { }"));

		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(root);

		Assert.AreEqual(PackageUsage.Used, Finding(report, "Spectre.Console").Usage);
	}

	[TestMethod]
	public void TreatsAParentNamespaceAsUse()
	{
		// Microsoft.Extensions.Logging.Abstractions ships the Microsoft.Extensions.Logging namespace.
		WriteProject(
			"Sample",
			"""    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />""",
			("Program.cs", "using Microsoft.Extensions.Logging;\n\nnamespace Sample; internal static class Program { }"));

		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(root);

		Assert.AreEqual(PackageUsage.Used, Finding(report, "Microsoft.Extensions.Logging.Abstractions").Usage);
	}

	[TestMethod]
	public void TreatsAGluedIdSuffixAsUse()
	{
		// LibreHardwareMonitorLib ships the LibreHardwareMonitor.* namespaces.
		WriteProject(
			"Sample",
			"""    <PackageReference Include="LibreHardwareMonitorLib" Version="0.9.4" />""",
			("Program.cs", "using LibreHardwareMonitor.Hardware;\n\nnamespace Sample; internal static class Program { }"));

		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(root);

		Assert.AreEqual(PackageUsage.Used, Finding(report, "LibreHardwareMonitorLib").Usage);
	}

	[TestMethod]
	public void DoesNotTreatASharedRootNamespaceAsUse()
	{
		// ktsu.Extensions must not vouch for the unrelated ktsu.Semantics.Strings.
		WriteProject(
			"Sample",
			"""
			    <PackageReference Include="ktsu.Extensions" Version="1.0.0" />
			    <PackageReference Include="ktsu.Semantics.Strings" Version="5.3.1" />
			""",
			("Program.cs", "using ktsu.Extensions;\n\nnamespace Sample; internal static class Program { }"));

		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(root);

		Assert.AreEqual(PackageUsage.Used, Finding(report, "ktsu.Extensions").Usage);
		Assert.AreEqual(PackageUsage.Unused, Finding(report, "ktsu.Semantics.Strings").Usage);
	}

	[TestMethod]
	public void CountsAGlobalUsingAndAnMsBuildUsingItem()
	{
		string projectDirectory = Path.Combine(root, "Sample");
		Directory.CreateDirectory(projectDirectory);
		File.WriteAllText(Path.Combine(projectDirectory, "Sample.csproj"), """
			<Project Sdk="Microsoft.NET.Sdk">
			  <ItemGroup>
			    <PackageReference Include="Moq" Version="4.20.72" />
			    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
			    <Using Include="Newtonsoft.Json" />
			  </ItemGroup>
			</Project>
			""");
		File.WriteAllText(Path.Combine(projectDirectory, "Usings.cs"), "global using Moq;\n");

		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(root);

		Assert.AreEqual(PackageUsage.Used, Finding(report, "Moq").Usage);
		Assert.AreEqual(PackageUsage.Used, Finding(report, "Newtonsoft.Json").Usage);
	}

	[TestMethod]
	public void HoldsBackAnalyzersAndBuildTimeOnlyReferences()
	{
		WriteProject(
			"Sample",
			"""
			    <PackageReference Include="StyleCop.Analyzers" Version="1.2.0" />
			    <PackageReference Include="Polyfill" Version="7.0.0" />
			    <PackageReference Include="Custom.Tasks" Version="1.0.0" PrivateAssets="all" />
			    <PackageReference Include="Other.Thing" Version="1.0.0" ExcludeAssets="compile" />
			""",
			("Program.cs", "namespace Sample; internal static class Program { }"));

		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(root);

		Assert.IsFalse(report.Unused.Any(), "Build-time-only packages must never be reported as unused.");
		Assert.AreEqual(4, report.BuildTimeOnly.Count());
		Assert.AreEqual(PackageUsage.BuildTimeOnly, Finding(report, "StyleCop.Analyzers").Usage);
		Assert.AreEqual("PrivateAssets=\"all\"", Finding(report, "Custom.Tasks").Reason);
		Assert.AreEqual("Compile assets excluded", Finding(report, "Other.Thing").Reason);
	}

	[TestMethod]
	public void HoldsBackAReferenceWhoseIncludeAssetsOmitCompile()
	{
		WriteProject(
			"Sample",
			"""    <PackageReference Include="Some.Generator.Pack" Version="1.0.0" IncludeAssets="build;buildTransitive" />""",
			("Program.cs", "namespace Sample; internal static class Program { }"));

		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(root);

		Assert.AreEqual(PackageUsage.BuildTimeOnly, Finding(report, "Some.Generator.Pack").Usage);
		Assert.AreEqual("IncludeAssets omits compile assets", Finding(report, "Some.Generator.Pack").Reason);
	}

	[TestMethod]
	public void IgnoresBuildOutputUnderBinAndObj()
	{
		string projectPath = WriteProject(
			"Sample",
			"""    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />""",
			("Program.cs", "namespace Sample; internal static class Program { }"));

		string generated = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "Debug");
		Directory.CreateDirectory(generated);
		File.WriteAllText(Path.Combine(generated, "Generated.cs"), "using Newtonsoft.Json;\n");

		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(root);

		Assert.AreEqual(PackageUsage.Unused, Finding(report, "Newtonsoft.Json").Usage);
	}

	[TestMethod]
	public void ScansEveryProjectBeneathAWorkspace()
	{
		WriteProject(
			"UsesIt",
			"""    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />""",
			("Program.cs", "using Newtonsoft.Json;\n"));
		WriteProject(
			"DoesNot",
			"""    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />""",
			("Program.cs", "namespace DoesNot; internal static class Program { }"));

		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(root);

		Assert.AreEqual(2, report.ProjectsScanned);
		PackageFinding unused = report.Unused.Single();
		Assert.IsTrue(unused.ProjectPath.Contains("DoesNot", StringComparison.Ordinal));
	}

	[TestMethod]
	public void ScansASingleProjectFileWhenPointedAtOne()
	{
		string projectPath = WriteProject(
			"Sample",
			"""    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />""",
			("Program.cs", "namespace Sample; internal static class Program { }"));

		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(projectPath);

		Assert.AreEqual(1, report.ProjectsScanned);
		Assert.AreEqual("Newtonsoft.Json", report.Unused.Single().PackageId);
	}

	[TestMethod]
	public void ReportsAPackageVersionNoProjectReferences()
	{
		WriteProject(
			"Sample",
			"""    <PackageReference Include="Newtonsoft.Json" />""",
			("Program.cs", "using Newtonsoft.Json;\n"));

		File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), """
			<Project>
			  <ItemGroup>
			    <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
			    <PackageVersion Include="Nobody.Uses.This" Version="1.0.0" />
			  </ItemGroup>
			</Project>
			""");

		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(root);

		Assert.AreEqual(1, report.OrphanedPackageVersions.Count);
		Assert.AreEqual("Nobody.Uses.This", report.OrphanedPackageVersions[0]);
	}

	[TestMethod]
	public void DoesNotOrphanAPackageVersionThatDirectoryBuildPropsReferences()
	{
		WriteProject(
			"Sample",
			"""    <PackageReference Include="Newtonsoft.Json" />""",
			("Program.cs", "using Newtonsoft.Json;\n"));

		File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
			<Project>
			  <ItemGroup>
			    <PackageReference Include="Polyfill" PrivateAssets="all" />
			  </ItemGroup>
			</Project>
			""");
		File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), """
			<Project>
			  <ItemGroup>
			    <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
			    <PackageVersion Include="Polyfill" Version="7.0.0" />
			  </ItemGroup>
			</Project>
			""");

		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(root);

		Assert.AreEqual(0, report.OrphanedPackageVersions.Count);
	}

	[TestMethod]
	public void ReturnsAnEmptyReportWhenThereAreNoProjects()
	{
		UnusedPackageReport report = UnusedPackageAnalyzer.Analyze(root);

		Assert.AreEqual(0, report.ProjectsScanned);
		Assert.AreEqual(0, report.Findings.Count);
	}
}
