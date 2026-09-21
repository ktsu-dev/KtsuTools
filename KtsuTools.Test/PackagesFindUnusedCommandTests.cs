// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using KtsuTools.Commands;
using KtsuTools.Core.Services.Process;
using KtsuTools.Packages;
using Moq;
using Spectre.Console.Cli;

[TestClass]
public class PackagesFindUnusedCommandTests
{
	private string root = string.Empty;

	[TestInitialize]
	public void CreateWorkspace()
	{
		root = Path.Combine(Path.GetTempPath(), "ktsu-find-unused-cmd-" + Guid.NewGuid().ToString("N"));
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

	private string WriteProject()
	{
		string projectDirectory = Path.Combine(root, "Sample");
		Directory.CreateDirectory(projectDirectory);

		string projectPath = Path.Combine(projectDirectory, "Sample.csproj");
		File.WriteAllText(projectPath, """
			<Project Sdk="Microsoft.NET.Sdk">
			  <ItemGroup>
			    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
			  </ItemGroup>
			</Project>
			""");
		File.WriteAllText(Path.Combine(projectDirectory, "Program.cs"), "namespace Sample; internal static class Program { }");

		return projectPath;
	}

	private static CommandContext Context() => new([], new NoRemainingArguments(), "find-unused", data: null);

	[TestMethod]
	public async Task ScansADirectoryWhenPathIsOne()
	{
		WriteProject();
		ICommand<PackagesFindUnusedCommand.Settings> command =
			new PackagesFindUnusedCommand(new PackagesService(Mock.Of<IProcessService>()));

		int exit = 0;
		string output = await ConsoleCapture.CaptureAsync(async () =>
			exit = await command
				.ExecuteAsync(Context(), new PackagesFindUnusedCommand.Settings { Path = root }, CancellationToken.None)
				.ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		StringAssert.Contains(output, "Scanned 1 project(s).", StringComparison.Ordinal);
		StringAssert.Contains(output, "Newtonsoft.Json", StringComparison.Ordinal);
	}

	[TestMethod]
	public async Task ScansASingleProjectWhenPathIsAFile()
	{
		string projectPath = WriteProject();
		ICommand<PackagesFindUnusedCommand.Settings> command =
			new PackagesFindUnusedCommand(new PackagesService(Mock.Of<IProcessService>()));

		int exit = 0;
		string output = await ConsoleCapture.CaptureAsync(async () =>
			exit = await command
				.ExecuteAsync(Context(), new PackagesFindUnusedCommand.Settings { Path = projectPath }, CancellationToken.None)
				.ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		StringAssert.Contains(output, "Scanned 1 project(s).", StringComparison.Ordinal);
	}

	[TestMethod]
	public async Task PassesTheBuildTimeFlagThrough()
	{
		string projectDirectory = Path.Combine(root, "Sample");
		Directory.CreateDirectory(projectDirectory);
		await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.csproj"), """
			<Project Sdk="Microsoft.NET.Sdk">
			  <ItemGroup>
			    <PackageReference Include="StyleCop.Analyzers" Version="1.2.0" />
			  </ItemGroup>
			</Project>
			""").ConfigureAwait(false);

		ICommand<PackagesFindUnusedCommand.Settings> command =
			new PackagesFindUnusedCommand(new PackagesService(Mock.Of<IProcessService>()));

		string output = await ConsoleCapture.CaptureAsync(() => command
			.ExecuteAsync(
				Context(),
				new PackagesFindUnusedCommand.Settings { Path = root, ShowBuildTime = true },
				CancellationToken.None)).ConfigureAwait(false);

		StringAssert.Contains(output, "StyleCop.Analyzers", StringComparison.Ordinal);
	}

	[TestMethod]
	public void ShowBuildTimeDefaultsToOff()
	{
		PackagesFindUnusedCommand.Settings settings = new() { Path = "." };

		Assert.IsFalse(settings.ShowBuildTime, "The report should stay free of build-time-only noise unless asked.");
	}
}
