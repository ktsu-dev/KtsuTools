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
public class RepoGitTests
{
	[TestMethod]
	public async Task RunGitAsyncRunsOncePerRepositoryInItsOwnDirectory()
	{
		using TempTree tree = TempTree.WithRepos("repo-a", Path.Combine("nested", "repo-b"));
		tree.CreatePlainDirectory("not-a-repo");

		RecordingProcessService fake = new();
		int exit = await CreateService(fake).RunGitAsync(tree.Root, ["status"], color: false).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		Assert.AreEqual(2, fake.Calls.Count, "Should run git once per discovered repository.");
		CollectionAssert.AreEquivalent(
			new[] { tree.At("repo-a"), tree.At("nested", "repo-b") },
			fake.Calls.Select(c => c.WorkingDirectory).ToArray(),
			"Each invocation should use its own repository as the working directory.");
		Assert.IsTrue(fake.Calls.All(c => c.Command == "git"), "Should invoke git, not another executable.");
	}

	[TestMethod]
	public async Task RunGitAsyncForwardsArgumentsInOrder()
	{
		using TempTree tree = TempTree.WithRepos("repo-a");

		RecordingProcessService fake = new();
		await CreateService(fake).RunGitAsync(tree.Root, ["fetch", "--prune", "--all"], color: false).ConfigureAwait(false);

		Assert.AreEqual("fetch --prune --all", fake.Calls[0].Arguments);
	}

	[TestMethod]
	public async Task RunGitAsyncQuotesArgumentsContainingSpaces()
	{
		using TempTree tree = TempTree.WithRepos("repo-a");

		RecordingProcessService fake = new();
		await CreateService(fake).RunGitAsync(tree.Root, ["commit", "-m", "a message"], color: false).ConfigureAwait(false);

		Assert.AreEqual("commit -m \"a message\"", fake.Calls[0].Arguments);
	}

	[TestMethod]
	public async Task RunGitAsyncPrependsColorOverrideWhenColorEnabled()
	{
		using TempTree tree = TempTree.WithRepos("repo-a");

		RecordingProcessService fake = new();
		await CreateService(fake).RunGitAsync(tree.Root, ["status"], color: true).ConfigureAwait(false);

		Assert.AreEqual(
			"-c color.ui=always status",
			fake.Calls[0].Arguments,
			"The -c override must precede the subcommand or git rejects it.");
	}

	[TestMethod]
	public async Task RunGitAsyncReturnsNonZeroWhenAnyRepositoryFails()
	{
		using TempTree tree = TempTree.WithRepos("good", "bad");

		RecordingProcessService fake = new(dir => Path.GetFileName(dir) == "bad" ? 1 : 0);
		int exit = await CreateService(fake).RunGitAsync(tree.Root, ["status"], color: false).ConfigureAwait(false);

		Assert.AreEqual(1, exit, "A failure in any repository should surface in the aggregate exit code.");
		Assert.AreEqual(2, fake.Calls.Count, "A failing repository should not stop the remaining ones.");
	}

	[TestMethod]
	public async Task RunGitAsyncRunsOnlyOnRootWhenRootIsItselfARepository()
	{
		using TempTree tree = TempTree.WithRepos("repo-a");
		Directory.CreateDirectory(Path.Combine(tree.RootDirectory, ".git"));

		RecordingProcessService fake = new();
		await CreateService(fake).RunGitAsync(tree.Root, ["status"], color: false).ConfigureAwait(false);

		Assert.AreEqual(1, fake.Calls.Count, "A repository root should not recurse into nested repositories.");
		Assert.AreEqual(tree.RootDirectory, fake.Calls[0].WorkingDirectory);
	}

	[TestMethod]
	public async Task RunGitAsyncReturnsZeroWhenNoRepositoriesFound()
	{
		using TempTree tree = TempTree.Empty();
		tree.CreatePlainDirectory("just-files");

		RecordingProcessService fake = new();
		int exit = await CreateService(fake).RunGitAsync(tree.Root, ["status"], color: false).ConfigureAwait(false);

		Assert.AreEqual(0, exit, "Finding nothing to do is not a failure.");
		Assert.AreEqual(0, fake.Calls.Count);
	}

	[TestMethod]
	public async Task RunGitAsyncReturnsNonZeroWhenNoArgumentsGiven()
	{
		using TempTree tree = TempTree.WithRepos("repo-a");

		RecordingProcessService fake = new();
		int exit = await CreateService(fake).RunGitAsync(tree.Root, [], color: false).ConfigureAwait(false);

		Assert.AreEqual(1, exit, "An empty git command line is a usage error.");
		Assert.AreEqual(0, fake.Calls.Count);
	}

	[TestMethod]
	public async Task RunGitAsyncReturnsNonZeroWhenDirectoryMissing()
	{
		string missing = Path.Combine(Path.GetTempPath(), $"ktsu_missing_{Guid.NewGuid():N}");
		AbsoluteDirectoryPath missingPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(missing);

		RecordingProcessService fake = new();
		int exit = await CreateService(fake).RunGitAsync(missingPath, ["status"], color: false).ConfigureAwait(false);

		Assert.AreEqual(1, exit);
		Assert.AreEqual(0, fake.Calls.Count);
	}

	[TestMethod]
	public async Task RunGitAsyncTreatsGitFileAsRepository()
	{
		using TempTree tree = TempTree.Empty();
		string worktree = tree.CreatePlainDirectory("worktree");

		// Worktrees and submodules store .git as a file pointing at the real git directory.
		await File.WriteAllTextAsync(Path.Combine(worktree, ".git"), "gitdir: ../real/.git").ConfigureAwait(false);

		RecordingProcessService fake = new();
		await CreateService(fake).RunGitAsync(tree.Root, ["status"], color: false).ConfigureAwait(false);

		Assert.AreEqual(1, fake.Calls.Count, "A .git file marks a worktree or submodule, which is still a repository.");
		Assert.AreEqual(worktree, fake.Calls[0].WorkingDirectory);
	}

	private static RepoService CreateService(IProcessService processService) =>
		new(new Mock<IGitService>().Object, processService);

	private sealed class RecordingProcessService(Func<string, int>? exitCodeForWorkingDirectory = null) : IProcessService
	{
		public List<(string Command, string Arguments, string? WorkingDirectory)> Calls { get; } = [];

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			RunAsync(command, arguments, workingDirectory, null, ct);

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default)
		{
			lock (Calls)
			{
				Calls.Add((command, arguments, workingDirectory));
			}

			int exit = exitCodeForWorkingDirectory is not null && workingDirectory is not null
				? exitCodeForWorkingDirectory(workingDirectory)
				: 0;

			return Task.FromResult(new ProcessResult(exit, [], []));
		}
	}

	private sealed class TempTree : IDisposable
	{
		private TempTree(string root) => RootDirectory = root;

		public string RootDirectory { get; }

		public AbsoluteDirectoryPath Root => AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(RootDirectory);

		public static TempTree Empty()
		{
			string root = Path.Combine(Path.GetTempPath(), $"ktsu_git_{Guid.NewGuid():N}");
			Directory.CreateDirectory(root);
			return new TempTree(root);
		}

		public static TempTree WithRepos(params string[] relativePaths)
		{
			TempTree tree = Empty();
			foreach (string relative in relativePaths)
			{
				Directory.CreateDirectory(Path.Combine(tree.RootDirectory, relative, ".git"));
			}

			return tree;
		}

		public string CreatePlainDirectory(string relative)
		{
			string full = Path.Combine(RootDirectory, relative);
			Directory.CreateDirectory(full);
			return full;
		}

		public string At(params string[] segments) =>
			Path.Combine([RootDirectory, .. segments]);

		public void Dispose()
		{
			try
			{
				Directory.Delete(RootDirectory, recursive: true);
			}
			catch (DirectoryNotFoundException)
			{
				// Already gone.
			}
		}
	}
}
