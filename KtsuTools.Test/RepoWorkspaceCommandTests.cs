// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Commands;
using KtsuTools.Core.Services.Git;
using KtsuTools.Core.Services.Process;
using KtsuTools.Repo;
using Moq;
using Spectre.Console.Cli;

[TestClass]
public class RepoWorkspaceCommandTests
{
	[TestMethod]
	public void SettingsDefaultToTheConventionalWorkspaceRoot()
	{
		RepoWorkspaceCommand.Settings settings = new();

		Assert.AreEqual("c:/dev/ktsu-dev", settings.Path, "A bare workspace verb should fall back to the conventional root.");
	}

	[TestMethod]
	public async Task LfsInstallResolvesTheWorkspaceAndDelegatesToTheService()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_lfscmd_{Guid.NewGuid():N}");
		string repo = Path.Join(root, "alpha");
		Directory.CreateDirectory(Path.Join(repo, ".git"));

		try
		{
			Mock<IProcessService> process = new();
			process
				.Setup(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProcessResult(0, [], []));

			RepoService service = new(new Mock<IGitService>().Object, process.Object);
			ICommand<RepoWorkspaceCommand.Settings> command = new RepoLfsInstallCommand(service);
			CommandContext context = new([], new NoRemainingArguments(), "install", data: null);

			int exit = await command
				.ExecuteAsync(context, new RepoWorkspaceCommand.Settings { Path = root }, CancellationToken.None)
				.ConfigureAwait(false);

			Assert.AreEqual(0, exit, "Every repository was configured, so the verb should succeed.");
			process.Verify(
				p => p.RunAsync("git", "lfs install --local", repo, It.IsAny<CancellationToken>()),
				Times.Once,
				"The verb should resolve --path to a workspace root and configure the repository found under it.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}
}
