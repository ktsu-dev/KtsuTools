// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ktsu.Semantics.Paths;
using KtsuTools.Sync;
using LibGit2Sharp;

[TestClass]
public class SyncServiceTests
{
	[TestMethod]
	public void HashToStringEmptyArrayReturnsEmpty()
	{
		string result = SyncService.HashToString([]);
		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void HashToStringSingleByteReturnsTwoUppercaseHexChars()
	{
		Assert.AreEqual("0F", SyncService.HashToString([0x0F]));
		Assert.AreEqual("FF", SyncService.HashToString([0xFF]));
		Assert.AreEqual("00", SyncService.HashToString([0x00]));
	}

	[TestMethod]
	public void HashToStringKnownBytesProducesExpectedHex()
	{
		byte[] bytes = [0xDE, 0xAD, 0xBE, 0xEF];
		Assert.AreEqual("DEADBEEF", SyncService.HashToString(bytes));
	}

	[TestMethod]
	public void IsRepoNestedNoGitInChainReturnsFalse()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_nested_{Guid.NewGuid():N}");
		string leaf = Path.Combine(root, "a", "b", "c");
		Directory.CreateDirectory(leaf);
		try
		{
			AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(leaf);
			Assert.IsFalse(SyncService.IsRepoNested(path));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public void IsRepoNestedSingleRepoAncestorReturnsFalse()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_nested_{Guid.NewGuid():N}");
		string repoRoot = Path.Combine(root, "repo");
		string leaf = Path.Combine(repoRoot, "src");
		Directory.CreateDirectory(leaf);
		Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
		try
		{
			AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(leaf);
			Assert.IsFalse(SyncService.IsRepoNested(path));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public void IsRepoNestedTwoRepoAncestorsReturnsTrue()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_nested_{Guid.NewGuid():N}");
		string outerRepo = Path.Combine(root, "outer");
		string innerRepo = Path.Combine(outerRepo, "inner");
		string leaf = Path.Combine(innerRepo, "src");
		Directory.CreateDirectory(leaf);
		Directory.CreateDirectory(Path.Combine(outerRepo, ".git"));
		Directory.CreateDirectory(Path.Combine(innerRepo, ".git"));
		try
		{
			AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(leaf);
			Assert.IsTrue(SyncService.IsRepoNested(path));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public void SwitchToSyncBranchCreatesTheBranchAndLeavesTheSyncedFileStaged()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		string originalBranch = repo.CurrentBranch;
		repo.Write("shared.txt", "synced content");

		SyncService.BranchSwitch? branchSwitch = SyncService.SwitchToSyncBranch(repo.Root, "sync/shared");

		Assert.IsNotNull(branchSwitch);
		Assert.AreEqual(originalBranch, branchSwitch.OriginalBranch, "The branch to come back to must be recorded.");
		Assert.AreEqual("sync/shared", repo.CurrentBranch, "Commits must land on the sync branch, not the checked-out one.");
		Assert.AreEqual("synced content", repo.Read("shared.txt"), "Checking out the branch must not discard the synced file.");
	}

	[TestMethod]
	public void SwitchToSyncBranchReusesAnExistingBranchInsteadOfClobberingIt()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		string existingTip = repo.CommitOnBranch("sync/shared", "earlier.txt", "from a previous run", "KtsuTools");

		SyncService.BranchSwitch? branchSwitch = SyncService.SwitchToSyncBranch(repo.Root, "sync/shared");

		Assert.IsNotNull(branchSwitch);
		Assert.AreEqual("sync/shared", repo.CurrentBranch);
		Assert.AreEqual(existingTip, repo.HeadSha, "An existing sync branch must be reused at its own tip, not reset to HEAD.");
	}

	[TestMethod]
	public void SwitchToSyncBranchOnTheSyncBranchAlreadyIsANoOp()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		_ = repo.CommitOnBranch("sync/shared", "earlier.txt", "from a previous run", "KtsuTools");
		repo.Checkout("sync/shared");

		SyncService.BranchSwitch? branchSwitch = SyncService.SwitchToSyncBranch(repo.Root, "sync/shared");

		Assert.IsNotNull(branchSwitch);
		Assert.AreEqual("sync/shared", branchSwitch.OriginalBranch, "Restoring must be a no-op when sync did not move the repo.");
		Assert.AreEqual("sync/shared", repo.CurrentBranch);
	}

	[TestMethod]
	public void RestoreBranchReturnsToTheOriginalBranchAndKeepsTheSyncCommit()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		string originalBranch = repo.CurrentBranch;
		string originalTip = repo.HeadSha;

		SyncService.BranchSwitch? branchSwitch = SyncService.SwitchToSyncBranch(repo.Root, "sync/shared");
		Assert.IsNotNull(branchSwitch);
		string syncTip = repo.Commit("shared.txt", "synced content", "KtsuTools");

		SyncService.RestoreBranch(branchSwitch);

		Assert.AreEqual(originalBranch, repo.CurrentBranch, "The repo must be left on the branch the user had checked out.");
		Assert.AreEqual(originalTip, repo.HeadSha, "The original branch must not have gained the sync commit.");
		Assert.AreEqual(syncTip, repo.TipOf("sync/shared"), "The sync commit must survive on the sync branch.");
	}

	[TestMethod]
	public void CommitAuthorsSinceBaseCountsOnlyWhatTheSyncAdded()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		string baseTip = repo.HeadSha;

		_ = SyncService.SwitchToSyncBranch(repo.Root, "sync/shared");
		_ = repo.Commit("shared.txt", "synced content", "KtsuTools");
		_ = repo.Commit("other.txt", "also synced", "KtsuTools");

		IReadOnlyList<string> authors = SyncService.CommitAuthorsSinceBase(repo.Root, baseTip);
		string[] expected = ["KtsuTools", "KtsuTools"];

		CollectionAssert.AreEqual(
			expected,
			authors.ToArray(),
			"Only the commits added on top of the base belong to this sync.");
	}

	[TestMethod]
	public void CommitAuthorsSinceBaseIsEmptyWhenTheSyncCommittedNothing()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		string baseTip = repo.HeadSha;

		_ = SyncService.SwitchToSyncBranch(repo.Root, "sync/shared");

		Assert.AreEqual(0, SyncService.CommitAuthorsSinceBase(repo.Root, baseTip).Count);
	}

	[TestMethod]
	public void BuildPushArgumentsSetsUpstreamForASyncBranch()
	{
		Assert.AreEqual(
			"push --set-upstream origin sync/shared",
			SyncService.BuildPushArguments("sync/shared"),
			"A branch sync just created has no upstream, so the first push must set one.");
	}

	[TestMethod]
	public void BuildPushArgumentsIsAPlainPushWithoutABranch()
	{
		Assert.AreEqual("push", SyncService.BuildPushArguments(string.Empty));
	}

	[TestMethod]
	public void RepoRootsForCollapsesFilesSharingARepositoryAndSkipsUntrackedOnes()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		string outside = Path.Combine(Path.GetTempPath(), $"ktsu_sync_loose_{Guid.NewGuid():N}");
		Directory.CreateDirectory(outside);

		try
		{
			repo.Write("other.txt", "second file");
			File.WriteAllText(Path.Combine(outside, "shared.txt"), "not in a repo");

			IReadOnlyList<string> roots = SyncService.RepoRootsFor(
			[
				Path.Combine(repo.Root, "shared.txt"),
				Path.Combine(repo.Root, "other.txt"),
				Path.Combine(outside, "shared.txt"),
			]);

			Assert.AreEqual(1, roots.Count, "Two files in one repo must yield one checkout, not two.");
			Assert.AreEqual(
				Path.GetFileName(repo.Root),
				Path.GetFileName(roots[0].TrimEnd(Path.DirectorySeparatorChar)));
		}
		finally
		{
			Directory.Delete(outside, recursive: true);
		}
	}

	/// <summary>
	/// A throwaway git repository with real commits, so the branch handling is exercised against
	/// libgit2 rather than a stand-in.
	/// </summary>
	private sealed class TempGitRepo : IDisposable
	{
		private TempGitRepo(string root) => Root = root;

		public string Root { get; }

		public static TempGitRepo WithInitialCommit()
		{
			string root = Path.Combine(Path.GetTempPath(), $"ktsu_sync_{Guid.NewGuid():N}");
			Directory.CreateDirectory(root);
			_ = Repository.Init(root);

			TempGitRepo repo = new(root);
			_ = repo.Commit("shared.txt", "original content", "A Human");
			return repo;
		}

		public string CurrentBranch
		{
			get
			{
				using Repository repo = new(Root);
				return repo.Head.FriendlyName;
			}
		}

		public string HeadSha
		{
			get
			{
				using Repository repo = new(Root);
				return repo.Head.Tip.Sha;
			}
		}

		public string TipOf(string branchName)
		{
			using Repository repo = new(Root);
			return repo.Branches[branchName].Tip.Sha;
		}

		public void Write(string fileName, string content) =>
			File.WriteAllText(Path.Combine(Root, fileName), content);

		public string Read(string fileName) =>
			File.ReadAllText(Path.Combine(Root, fileName));

		public string Commit(string fileName, string content, string author)
		{
			Write(fileName, content);
			using Repository repo = new(Root);
			Commands.Stage(repo, fileName);
			Signature signature = new(author, $"{author}@example.test", DateTimeOffset.Now);
			return repo.Commit($"Sync {fileName}", signature, signature).Sha;
		}

		public void Checkout(string branchName)
		{
			using Repository repo = new(Root);
			_ = Commands.Checkout(repo, repo.Branches[branchName]);
		}

		/// <summary>Commits on a branch, creating it first, and leaves the repo where it started.</summary>
		public string CommitOnBranch(string branchName, string fileName, string content, string author)
		{
			string startingBranch = CurrentBranch;

			using (Repository repo = new(Root))
			{
				_ = Commands.Checkout(repo, repo.CreateBranch(branchName));
			}

			string sha = Commit(fileName, content, author);
			Checkout(startingBranch);
			return sha;
		}

		public void Dispose()
		{
			try
			{
				// Git marks objects and pack files read-only, which blocks a plain recursive delete.
				foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
				{
					File.SetAttributes(file, FileAttributes.Normal);
				}

				Directory.Delete(Root, recursive: true);
			}
			catch (DirectoryNotFoundException)
			{
				// Already gone.
			}
		}
	}
}
