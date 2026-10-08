// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Semantics.Paths;
using KtsuTools.Core.Services.Git;
using KtsuTools.Core.Services.Process;
using KtsuTools.Repo;
using Moq;

[TestClass]
public class RepoUpdatePackagesTests
{
	// Captured from 'dotnet list p.csproj package --outdated --format json' against nuget.org.
	private static readonly string[] OutdatedJson =
	[
		"{",
		"  \"version\": 1,",
		"  \"parameters\": \"--outdated\",",
		"  \"sources\": [",
		"    \"https://api.nuget.org/v3/index.json\"",
		"  ],",
		"  \"projects\": [",
		"    {",
		"      \"path\": \"/src/p/p.csproj\",",
		"      \"frameworks\": [",
		"        {",
		"          \"framework\": \"net10.0\",",
		"          \"topLevelPackages\": [",
		"            {",
		"              \"id\": \"Newtonsoft.Json\",",
		"              \"requestedVersion\": \"12.0.1\",",
		"              \"resolvedVersion\": \"12.0.1\",",
		"              \"latestVersion\": \"13.0.4\"",
		"            },",
		"            {",
		"              \"id\": \"Private.Package\",",
		"              \"requestedVersion\": \"1.0.0\",",
		"              \"resolvedVersion\": \"1.0.0\",",
		"              \"latestVersion\": \"Not found at the sources\"",
		"            }",
		"          ]",
		"        },",
		"        {",
		"          \"framework\": \"net9.0\",",
		"          \"topLevelPackages\": [",
		"            {",
		"              \"id\": \"Newtonsoft.Json\",",
		"              \"requestedVersion\": \"12.0.1\",",
		"              \"resolvedVersion\": \"12.0.1\",",
		"              \"latestVersion\": \"13.0.4\"",
		"            }",
		"          ]",
		"        }",
		"      ]",
		"    }",
		"  ]",
		"}",
	];

	private static readonly string[] NothingOutdatedJson =
	[
		"{ \"version\": 1, \"parameters\": \"--outdated\", \"projects\": [ { \"path\": \"/src/p/p.csproj\" } ] }",
	];

	[TestMethod]
	public void TryParseReadsEachOutdatedTopLevelPackageOnce()
	{
		bool parsed = OutdatedPackage.TryParse(OutdatedJson, out IReadOnlyList<OutdatedPackage> packages);

		Assert.IsTrue(parsed, "The captured --outdated JSON should parse.");
		CollectionAssert.AreEqual(
			new[] { new OutdatedPackage("Newtonsoft.Json", "13.0.4") },
			packages.ToArray(),
			"A package listed under two frameworks is one update, and one with no latest version is none.");
	}

	[TestMethod]
	public void TryParseReturnsNoPackagesWhenNothingIsOutdated()
	{
		Assert.IsTrue(OutdatedPackage.TryParse(NothingOutdatedJson, out IReadOnlyList<OutdatedPackage> packages));
		Assert.AreEqual(0, packages.Count);
	}

	[TestMethod]
	public void TryParseRejectsOutputThatIsNotTheExpectedJson()
	{
		Assert.IsFalse(OutdatedPackage.TryParse(null, out _), "No output means the outdated list is unknown.");
		Assert.IsFalse(OutdatedPackage.TryParse(["error: something went wrong"], out _));
		Assert.IsFalse(OutdatedPackage.TryParse(["{ \"projects\": ["], out _), "Truncated JSON is unreadable.");
		Assert.IsFalse(
			OutdatedPackage.TryParse(["{ \"version\": 1, \"problems\": [ { \"level\": \"error\", \"text\": \"No assets file\" } ], \"projects\": [] }"], out _),
			"An error reported in the JSON means the outdated list is incomplete.");
	}

	[TestMethod]
	public async Task UpdatePackagesAsyncAddsTheLatestVersionOfEachOutdatedPackage()
	{
		string root = CreateSolution("ktsu_update");
		try
		{
			FakeDotnet fake = new();
			RepoService service = new(new Mock<IGitService>().Object, fake);

			int exit = await service.UpdatePackagesAsync(AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root)).ConfigureAwait(false);

			Assert.AreEqual(0, exit, "Every step succeeded, so the exit code should be zero.");
			string project = Path.Join(root, "p", "p.csproj");
			Assert.IsTrue(
				fake.Calls.Contains($"add \"{project}\" package Newtonsoft.Json --version 13.0.4"),
				$"The outdated package should be updated to its latest version, but ran: {string.Join(" | ", fake.Calls)}");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task UpdatePackagesAsyncReturnsNonZeroWhenAnUpdateFails()
	{
		string root = CreateSolution("ktsu_update_addfail");
		try
		{
			RepoService service = new(new Mock<IGitService>().Object, new FakeDotnet(failing: "add"));

			int exit = await service.UpdatePackagesAsync(AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root)).ConfigureAwait(false);

			Assert.AreNotEqual(0, exit, "A failed package update should surface in the exit code.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task UpdatePackagesAsyncReturnsNonZeroWhenRestoreFails()
	{
		string root = CreateSolution("ktsu_update_restorefail");
		try
		{
			RepoService service = new(new Mock<IGitService>().Object, new FakeDotnet(failing: "restore"));

			int exit = await service.UpdatePackagesAsync(AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root)).ConfigureAwait(false);

			Assert.AreNotEqual(0, exit, "A failed restore should surface in the exit code.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task UpdatePackagesAsyncReturnsNonZeroWhenTheOutdatedListCannotBeRead()
	{
		string root = CreateSolution("ktsu_update_listfail");
		try
		{
			RepoService service = new(new Mock<IGitService>().Object, new FakeDotnet(failing: "list"));

			int exit = await service.UpdatePackagesAsync(AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root)).ConfigureAwait(false);

			Assert.AreNotEqual(0, exit, "Not knowing what is outdated is a failure, not a clean run.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task UpdatePackagesAsyncLeavesAnOutdatedProjectOnTheLatestVersion()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_update_real_{Guid.NewGuid():N}");
		string project = Path.Join(root, "p", "p.csproj");
		Directory.CreateDirectory(Path.Join(root, "p"));
		await File.WriteAllTextAsync(Path.Join(root, "r.slnx"), "<Solution>\n  <Project Path=\"p/p.csproj\" />\n</Solution>\n").ConfigureAwait(false);
		await File.WriteAllTextAsync(
			project,
			"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageReference Include="Newtonsoft.Json" Version="12.0.1" />
			  </ItemGroup>
			</Project>
			""").ConfigureAwait(false);
		try
		{
			RepoService service = new(new Mock<IGitService>().Object, new ProcessService());

			int exit = await service.UpdatePackagesAsync(AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root)).ConfigureAwait(false);

			string updated = await File.ReadAllTextAsync(project).ConfigureAwait(false);
			Assert.AreEqual(0, exit, "Updating and restoring should succeed.");
			Assert.DoesNotContain("12.0.1", updated, "The outdated version should have been replaced.");
			Assert.Contains("Newtonsoft.Json", updated, "The package should still be referenced.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	private static string CreateSolution(string prefix)
	{
		string root = Path.Join(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Join(root, "p"));
		File.WriteAllText(Path.Join(root, "r.slnx"), "<Solution>\n  <Project Path=\"p/p.csproj\" />\n</Solution>\n");
		File.WriteAllText(Path.Join(root, "p", "p.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
		return root;
	}

	/// <summary>
	/// Answers 'dotnet list package --outdated' with the captured JSON, and fails whichever dotnet
	/// verb it is told to.
	/// </summary>
	private sealed class FakeDotnet(string? failing = null) : IProcessService
	{
		public List<string> Calls { get; } = [];

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			RunAsync(command, arguments, workingDirectory, null, ct);

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default)
		{
			Calls.Add(arguments);

			if (failing is not null && arguments.StartsWith(failing + " ", StringComparison.Ordinal))
			{
				return Task.FromResult(new ProcessResult(1, [], ["error: simulated failure"]));
			}

			return Task.FromResult(arguments.StartsWith("list ", StringComparison.Ordinal)
				? new ProcessResult(0, OutdatedJson, [])
				: new ProcessResult(0, [], []));
		}
	}
}
