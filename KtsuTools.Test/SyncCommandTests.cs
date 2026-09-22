// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Commands;
using KtsuTools.Core.Services.Process;
using KtsuTools.Sync;
using Moq;
using Spectre.Console.Cli;

[TestClass]
public class SyncCommandTests
{
	[TestMethod]
	public void SettingsDefaultToAWorkspaceScanWithNothingExcluded()
	{
		SyncCommand.Settings settings = new();

		Assert.AreEqual(string.Empty, settings.Path);
		Assert.AreEqual(0, settings.Repo.Length, "A bare 'sync' names no repository explicitly.");
		Assert.AreEqual(string.Empty, settings.RepoList);
		Assert.AreEqual(0, settings.Exclude.Length, "A bare 'sync' excludes nothing.");
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ExecutingWithRepoFlagsScansEveryNamedRepository()
	{
		using TempScanTree first = TempScanTree.WithFile("repo-a", "shared.txt");
		using TempScanTree second = TempScanTree.WithFile("repo-b", "shared.txt");

		SyncCommand.Settings settings = new()
		{
			Repo = [first.RepoDirectory, second.RepoDirectory],
			Filename = ["shared.txt"],
		};

		(int exit, string output) = await ExecuteAsync(settings).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		StringAssert.Contains(output, first.RepoDirectory, StringComparison.Ordinal, "--repo must reach the scan.");
		StringAssert.Contains(output, second.RepoDirectory, StringComparison.Ordinal, "Every --repo must reach the scan, not just the first.");
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ExecutingWithARepoListReadsTheRepositoriesFromTheFile()
	{
		using TempScanTree tree = TempScanTree.WithFile("repo-a", "shared.txt");
		string listFile = Path.Join(tree.Root, "repos.txt");
		File.WriteAllLines(listFile, ["# the clones to sync", string.Empty, tree.RepoDirectory]);

		SyncCommand.Settings settings = new()
		{
			RepoList = listFile,
			Filename = ["shared.txt"],
			Exclude = ["third-party"],
		};

		(int exit, string output) = await ExecuteAsync(settings).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		StringAssert.Contains(output, tree.RepoDirectory, StringComparison.Ordinal, "--repo-list must reach the scan.");
		StringAssert.Contains(output, "third-party", StringComparison.Ordinal, "--exclude must reach the scan.");
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ExecutingWithAPathStillScansTheWorkspace()
	{
		using TempScanTree tree = TempScanTree.WithFile("repo-a", "shared.txt");

		SyncCommand.Settings settings = new()
		{
			Path = tree.Root,
			Filename = ["shared.txt"],
		};

		(int exit, string output) = await ExecuteAsync(settings).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		StringAssert.Contains(output, tree.Root, StringComparison.Ordinal, "--path must keep working on its own.");
	}

	private static async Task<(int Exit, string Output)> ExecuteAsync(SyncCommand.Settings settings)
	{
		SyncService service = new(new Mock<IProcessService>().Object);
		ICommand<SyncCommand.Settings> command = new SyncCommand(service);
		CommandContext context = new([], new NoRemainingArguments(), "sync", data: null);
		int exit = 0;

		string output = await ConsoleCapture.CaptureAsync(async () =>
			exit = await command.ExecuteAsync(context, settings, CancellationToken.None).ConfigureAwait(false))
			.ConfigureAwait(false);

		return (exit, output);
	}

	/// <summary>
	/// A throwaway workspace holding one repository directory with a single file in it, which is
	/// enough for the scan to run to completion without anything to reconcile.
	/// </summary>
	private sealed class TempScanTree : IDisposable
	{
		private TempScanTree(string root, string repoDirectory)
		{
			Root = root;
			RepoDirectory = repoDirectory;
		}

		public string Root { get; }

		public string RepoDirectory { get; }

		public static TempScanTree WithFile(string repoName, string fileName)
		{
			string root = Path.Join(Path.GetTempPath(), $"ktsu_sync_cmd_{Guid.NewGuid():N}");
			string repoDirectory = Path.Join(root, repoName);
			Directory.CreateDirectory(repoDirectory);
			File.WriteAllText(Path.Join(repoDirectory, fileName), "same content");
			return new TempScanTree(root, repoDirectory);
		}

		public void Dispose()
		{
			try
			{
				Directory.Delete(Root, recursive: true);
			}
			catch (DirectoryNotFoundException)
			{
				// Already gone.
			}
		}
	}
}
