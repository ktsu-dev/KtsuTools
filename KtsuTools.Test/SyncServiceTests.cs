// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Semantics.Paths;
using KtsuTools.Core.Services.Process;
using KtsuTools.Sync;
using Spectre.Console;

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
	public async Task SwitchToSyncBranchCreatesTheBranchAndLeavesTheSyncedFileStaged()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		string originalBranch = repo.CurrentBranch;
		repo.Write("shared.txt", "synced content");

		SyncService.BranchSwitch? branchSwitch = await SyncGit.SwitchToSyncBranchAsync(repo.Root, "sync/shared").ConfigureAwait(false);

		Assert.IsNotNull(branchSwitch);
		Assert.AreEqual(originalBranch, branchSwitch.OriginalBranch, "The branch to come back to must be recorded.");
		Assert.AreEqual("sync/shared", repo.CurrentBranch, "Commits must land on the sync branch, not the checked-out one.");
		Assert.AreEqual("synced content", repo.Read("shared.txt"), "Checking out the branch must not discard the synced file.");
	}

	[TestMethod]
	public async Task SwitchToSyncBranchReusesAnExistingBranchInsteadOfClobberingIt()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		string existingTip = repo.CommitOnBranch("sync/shared", "earlier.txt", "from a previous run", "KtsuTools");

		SyncService.BranchSwitch? branchSwitch = await SyncGit.SwitchToSyncBranchAsync(repo.Root, "sync/shared").ConfigureAwait(false);

		Assert.IsNotNull(branchSwitch);
		Assert.AreEqual("sync/shared", repo.CurrentBranch);
		Assert.AreEqual(existingTip, repo.HeadSha, "An existing sync branch must be reused at its own tip, not reset to HEAD.");
	}

	[TestMethod]
	public async Task SwitchToSyncBranchOnTheSyncBranchAlreadyIsANoOp()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		_ = repo.CommitOnBranch("sync/shared", "earlier.txt", "from a previous run", "KtsuTools");
		repo.Checkout("sync/shared");

		SyncService.BranchSwitch? branchSwitch = await SyncGit.SwitchToSyncBranchAsync(repo.Root, "sync/shared").ConfigureAwait(false);

		Assert.IsNotNull(branchSwitch);
		Assert.AreEqual("sync/shared", branchSwitch.OriginalBranch, "Restoring must be a no-op when sync did not move the repo.");
		Assert.AreEqual("sync/shared", repo.CurrentBranch);
	}

	[TestMethod]
	public async Task RestoreBranchReturnsToTheOriginalBranchAndKeepsTheSyncCommit()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		string originalBranch = repo.CurrentBranch;
		string originalTip = repo.HeadSha;

		SyncService.BranchSwitch? branchSwitch = await SyncGit.SwitchToSyncBranchAsync(repo.Root, "sync/shared").ConfigureAwait(false);
		Assert.IsNotNull(branchSwitch);
		string syncTip = repo.Commit("shared.txt", "synced content", "KtsuTools");

		await SyncGit.RestoreBranchAsync(branchSwitch).ConfigureAwait(false);

		Assert.AreEqual(originalBranch, repo.CurrentBranch, "The repo must be left on the branch the user had checked out.");
		Assert.AreEqual(originalTip, repo.HeadSha, "The original branch must not have gained the sync commit.");
		Assert.AreEqual(syncTip, repo.TipOf("sync/shared"), "The sync commit must survive on the sync branch.");
	}

	[TestMethod]
	public async Task CommitAuthorsSinceBaseCountsOnlyWhatTheSyncAdded()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		string baseTip = repo.HeadSha;

		_ = await SyncGit.SwitchToSyncBranchAsync(repo.Root, "sync/shared").ConfigureAwait(false);
		_ = repo.Commit("shared.txt", "synced content", "KtsuTools");
		_ = repo.Commit("other.txt", "also synced", "KtsuTools");

		IReadOnlyList<string> authors = await SyncGit.CommitAuthorsSinceBaseAsync(repo.Root, baseTip).ConfigureAwait(false);
		string[] expected = ["KtsuTools", "KtsuTools"];

		CollectionAssert.AreEqual(
			expected,
			authors.ToArray(),
			"Only the commits added on top of the base belong to this sync.");
	}

	[TestMethod]
	public async Task CommitAuthorsSinceBaseIsEmptyWhenTheSyncCommittedNothing()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		string baseTip = repo.HeadSha;

		_ = await SyncGit.SwitchToSyncBranchAsync(repo.Root, "sync/shared").ConfigureAwait(false);

		Assert.AreEqual(0, (await SyncGit.CommitAuthorsSinceBaseAsync(repo.Root, baseTip).ConfigureAwait(false)).Count);
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
	public async Task RepoRootsForCollapsesFilesSharingARepositoryAndSkipsUntrackedOnes()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		string outside = CreateCanonicalTempDirectory("ktsu_sync_loose");

		try
		{
			repo.Write("other.txt", "second file");
			await File.WriteAllTextAsync(Path.Join(outside, "shared.txt"), "not in a repo").ConfigureAwait(false);

			IReadOnlyList<string> roots = await SyncGit.RepoRootsForAsync(
			[
				Path.Join(repo.Root, "shared.txt"),
				Path.Join(repo.Root, "other.txt"),
				Path.Join(outside, "shared.txt"),
			]).ConfigureAwait(false);

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

	[TestMethod]
	public async Task CommitFilesPutsEachRepositoryCommitOnTheSyncBranch()
	{
		using TempGitRepo first = TempGitRepo.WithInitialCommit();
		using TempGitRepo second = TempGitRepo.WithInitialCommit();
		string firstOriginalTip = first.HeadSha;
		string secondOriginalBranch = second.CurrentBranch;

		first.Write("shared.txt", "synced content");
		second.Write("shared.txt", "synced content");

		IReadOnlyList<SyncService.BranchSwitch> switches = await SyncService.CommitFilesAsync(
			[Path.Join(first.Root, "shared.txt"), Path.Join(second.Root, "shared.txt")],
			"sync/shared").ConfigureAwait(false);

		Assert.AreEqual(2, switches.Count, "Each repository with a changed file must be switched.");
		Assert.AreNotEqual(firstOriginalTip, first.TipOf("sync/shared"), "The sync branch must carry the commit.");
		Assert.AreEqual(firstOriginalTip, first.TipOf(switches[0].OriginalBranch), "The original branch must not have moved.");
		Assert.AreEqual(secondOriginalBranch, switches[1].OriginalBranch);
	}

	/// <summary>
	/// The no-branch half of the auto-push decision: a repository is pushed without asking only
	/// when it is ahead of its upstream and every commit it is ahead by was written by the sync.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[TestMethod]
	public async Task FindPushableDirectoriesTakesOnlyRepositoriesAheadByTheSyncsOwnCommits()
	{
		string remote = CreateCanonicalTempDirectory("ktsu_sync_remote");
		string clones = CreateCanonicalTempDirectory("ktsu_sync_clones");

		try
		{
			TestGit.InitBare(remote);

			// A repository the remote has already seen, so each clone below has an upstream and
			// git can say how far ahead it is.
			string seed = Path.Join(clones, "seed");
			Directory.CreateDirectory(seed);
			TestGit.Init(seed);
			await File.WriteAllTextAsync(Path.Join(seed, "shared.txt"), "original").ConfigureAwait(false);
			_ = TestGit.Commit(seed, "shared.txt", "Add shared.txt", "A Human");
			TestGit.AddRemote(seed, "origin", remote);
			_ = TestGit.Run(seed, "push", "origin", "main");

			string pushable = Path.Join(clones, "pushable");
			string handEdited = Path.Join(clones, "hand-edited");
			string untouched = Path.Join(clones, "untouched");

			TestGit.Clone(remote, pushable);
			TestGit.Clone(remote, handEdited);
			TestGit.Clone(remote, untouched);

			await File.WriteAllTextAsync(Path.Join(pushable, "shared.txt"), "synced").ConfigureAwait(false);
			_ = TestGit.Commit(pushable, "shared.txt", "Sync shared.txt", SyncGit.CommitAuthorName);

			await File.WriteAllTextAsync(Path.Join(handEdited, "shared.txt"), "hand written").ConfigureAwait(false);
			_ = TestGit.Commit(handEdited, "shared.txt", "Edit shared.txt", "A Human");

			IReadOnlyList<string> pushDirectories =
			[
				.. await SyncGit.FindPushableDirectoriesAsync([pushable, handEdited, untouched]).ConfigureAwait(false)
			];

			Assert.AreEqual(1, pushDirectories.Count, "Only the repository the sync itself moved may be pushed unasked.");
			Assert.AreEqual(pushable, pushDirectories[0]);
		}
		finally
		{
			DeleteGitTree(clones);
			DeleteGitTree(remote);
		}
	}

	[TestMethod]
	public async Task RepoRootForIsNullForAPathThatNamesNothing()
	{
		Assert.IsNull(await SyncGit.RepoRootForAsync(string.Empty).ConfigureAwait(false));

		string outside = CreateCanonicalTempDirectory("ktsu_sync_norepo");
		try
		{
			Assert.IsNull(
				await SyncGit.RepoRootForAsync(outside).ConfigureAwait(false),
				"A directory that is not inside a repository has no root to report.");
		}
		finally
		{
			DeleteGitTree(outside);
		}
	}

	[TestMethod]
	public async Task CommitFilesAttributesItsCommitsToTheSyncSoTheyCanBePushedAutomatically()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		repo.Write("shared.txt", "synced content");

		IReadOnlyList<SyncService.BranchSwitch> switches = await SyncService.CommitFilesAsync(
			[Path.Join(repo.Root, "shared.txt")],
			"sync/shared").ConfigureAwait(false);

		Assert.AreEqual(1, switches.Count);

		// The author is not cosmetic: it is the only thing telling a sync's own commit from a
		// person's, so writing it as whoever happens to be configured locally would either strand
		// the branch or auto-push someone else's work.
		IReadOnlyList<string> authors =
			await SyncGit.CommitAuthorsSinceBaseAsync(repo.Root, switches[0].OriginalTipSha).ConfigureAwait(false);

		CollectionAssert.AreEqual(
			new[] { SyncGit.CommitAuthorName },
			authors.ToArray(),
			$"The sync's commit must be authored by {SyncGit.CommitAuthorName}, but was by {string.Join(", ", authors)}.");

		IReadOnlyList<string> pushDirectories =
			[.. await SyncService.FindPushableBranchDirectoriesAsync(switches).ConfigureAwait(false)];

		Assert.AreEqual(1, pushDirectories.Count, "A branch the sync wrote by itself is pushable without asking.");
		Assert.AreEqual(repo.Root, pushDirectories[0]);
	}

	[TestMethod]
	public async Task CommitFilesWithoutABranchCommitsOntoTheCheckedOutBranch()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		string originalBranch = repo.CurrentBranch;
		string originalTip = repo.HeadSha;
		repo.Write("shared.txt", "synced content");

		IReadOnlyList<SyncService.BranchSwitch> switches = await SyncService.CommitFilesAsync(
			[Path.Join(repo.Root, "shared.txt")],
			string.Empty).ConfigureAwait(false);

		Assert.AreEqual(0, switches.Count, "Committing in place switches nothing, so there is nothing to restore.");
		Assert.AreEqual(originalBranch, repo.CurrentBranch);
		Assert.AreNotEqual(originalTip, repo.HeadSha, "The commit must land on the checked-out branch.");
	}

	[TestMethod]
	public async Task SwitchReposToBranchSkipsARepositoryWithNothingToBranchFrom()
	{
		using TempGitRepo committed = TempGitRepo.WithInitialCommit();
		using TempGitRepo empty = TempGitRepo.WithoutAnyCommit();
		committed.Write("shared.txt", "synced content");
		empty.Write("shared.txt", "synced content");

		IReadOnlyList<SyncService.BranchSwitch> switches =
		[
			.. await SyncService.SwitchReposToBranchAsync(
				[Path.Join(committed.Root, "shared.txt"), Path.Join(empty.Root, "shared.txt")],
				"sync/shared").ConfigureAwait(false)
		];

		Assert.AreEqual(1, switches.Count, "A repo with no commit has no HEAD to branch from.");
		Assert.AreEqual("sync/shared", committed.CurrentBranch);
	}

	[TestMethod]
	public async Task CommitFilesLeavesARepositoryUncommittedWhenItsCheckoutConflicts()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		string originalBranch = repo.CurrentBranch;
		string originalTip = repo.HeadSha;

		// The sync branch already changed the same file, so checking it out over an uncommitted
		// local edit is a conflict libgit2 refuses.
		_ = repo.CommitOnBranch("sync/shared", "shared.txt", "a different version", "KtsuTools");
		repo.Write("shared.txt", "local edit");

		IReadOnlyList<SyncService.BranchSwitch> switches = await SyncService.CommitFilesAsync(
			[Path.Join(repo.Root, "shared.txt")],
			"sync/shared").ConfigureAwait(false);

		Assert.AreEqual(0, switches.Count, "A repo that could not be switched must not be reported as switched.");
		Assert.AreEqual(originalBranch, repo.CurrentBranch);
		Assert.AreEqual(originalTip, repo.HeadSha, "The commit must not land in place when the branch was the point.");
	}

	[TestMethod]
	public async Task RestoreBranchesReturnsEveryRepositoryAndSurvivesOneThatCannotBeRestored()
	{
		using TempGitRepo first = TempGitRepo.WithInitialCommit();
		using TempGitRepo second = TempGitRepo.WithInitialCommit();
		string firstOriginal = first.CurrentBranch;
		string secondOriginal = second.CurrentBranch;

		SyncService.BranchSwitch? firstSwitch = await SyncGit.SwitchToSyncBranchAsync(first.Root, "sync/shared").ConfigureAwait(false);
		SyncService.BranchSwitch? secondSwitch = await SyncGit.SwitchToSyncBranchAsync(second.Root, "sync/shared").ConfigureAwait(false);
		Assert.IsNotNull(firstSwitch);
		Assert.IsNotNull(secondSwitch);

		// A branch that no longer exists cannot be restored; the other repo must still come back.
		SyncService.BranchSwitch missing = new(first.Root, "branch-that-went-away", firstSwitch.OriginalTipSha);

		await SyncService.RestoreBranchesAsync([missing, secondSwitch]).ConfigureAwait(false);

		Assert.AreEqual("sync/shared", first.CurrentBranch, "The failed restore must be reported, not thrown.");
		Assert.AreEqual(secondOriginal, second.CurrentBranch, "A later repo must still be restored.");
		Assert.AreEqual(firstOriginal, secondOriginal);
	}

	[TestMethod]
	public async Task FindPushableBranchDirectoriesTakesOnlyBranchesHoldingSyncCommits()
	{
		using TempGitRepo pushable = TempGitRepo.WithInitialCommit();
		using TempGitRepo handEdited = TempGitRepo.WithInitialCommit();
		using TempGitRepo untouched = TempGitRepo.WithInitialCommit();

		SyncService.BranchSwitch? pushableSwitch = await SyncGit.SwitchToSyncBranchAsync(pushable.Root, "sync/shared").ConfigureAwait(false);
		SyncService.BranchSwitch? handEditedSwitch = await SyncGit.SwitchToSyncBranchAsync(handEdited.Root, "sync/shared").ConfigureAwait(false);
		SyncService.BranchSwitch? untouchedSwitch = await SyncGit.SwitchToSyncBranchAsync(untouched.Root, "sync/shared").ConfigureAwait(false);
		Assert.IsNotNull(pushableSwitch);
		Assert.IsNotNull(handEditedSwitch);
		Assert.IsNotNull(untouchedSwitch);

		_ = pushable.Commit("shared.txt", "synced content", "KtsuTools");
		_ = handEdited.Commit("shared.txt", "hand written", "A Human");

		IReadOnlyList<string> pushDirectories =
			[.. await SyncService.FindPushableBranchDirectoriesAsync([pushableSwitch, handEditedSwitch, untouchedSwitch]).ConfigureAwait(false)];

		Assert.AreEqual(1, pushDirectories.Count, "Only the branch carrying sync-authored commits may be pushed.");
		Assert.AreEqual(pushable.Root, pushDirectories[0]);
	}

	[TestMethod]
	public async Task PushDirectoryAsyncSetsUpstreamAndSkipsThePullForASyncBranch()
	{
		RecordingProcessService fake = new();

		await new SyncService(fake)
			.PushDirectoryAsync("/tmp/repo", "sync/shared", CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(1, fake.Calls.Count, "A branch the remote has never seen has nothing to pull.");
		Assert.AreEqual("push --set-upstream origin sync/shared", fake.Calls[0].Arguments);
		Assert.AreEqual("/tmp/repo", fake.Calls[0].WorkingDirectory);
	}

	[TestMethod]
	public async Task PushDirectoryAsyncStillPullsBeforePushingWithoutABranch()
	{
		RecordingProcessService fake = new();

		await new SyncService(fake)
			.PushDirectoryAsync("/tmp/repo", string.Empty, CancellationToken.None)
			.ConfigureAwait(false);

		string[] expected = ["pull", "push"];

		CollectionAssert.AreEqual(
			expected,
			fake.Calls.Select(c => c.Arguments).ToArray(),
			"Committing in place keeps the existing pull-then-push behaviour.");
	}

	[TestMethod]
	public async Task PushDirectoryAsyncSkipsThePushWhenThePullFails()
	{
		RecordingProcessService fake = new(exitCode: 1);

		await new SyncService(fake)
			.PushDirectoryAsync("/tmp/repo", string.Empty, CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(1, fake.Calls.Count, "A failed pull must not be followed by a push.");
		Assert.AreEqual("pull", fake.Calls[0].Arguments);
	}

	[TestMethod]
	public async Task PushToRemoteAsyncPushesEverySyncBranchWhenAutoPushIsOn()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		SyncService.BranchSwitch? branchSwitch = await SyncGit.SwitchToSyncBranchAsync(repo.Root, "sync/shared").ConfigureAwait(false);
		Assert.IsNotNull(branchSwitch);
		_ = repo.Commit("shared.txt", "synced content", "KtsuTools");

		RecordingProcessService fake = new();

		await new SyncService(fake)
			.PushToRemoteAsync([], autoPush: true, "sync/shared", [branchSwitch], CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(1, fake.Calls.Count);
		Assert.AreEqual("push --set-upstream origin sync/shared", fake.Calls[0].Arguments);
		Assert.AreEqual(repo.Root, fake.Calls[0].WorkingDirectory);
	}

	[TestMethod]
	public async Task PushToRemoteAsyncPushesNothingWhenNoSyncBranchGainedACommit()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		SyncService.BranchSwitch? branchSwitch = await SyncGit.SwitchToSyncBranchAsync(repo.Root, "sync/shared").ConfigureAwait(false);
		Assert.IsNotNull(branchSwitch);

		RecordingProcessService fake = new();

		await new SyncService(fake)
			.PushToRemoteAsync([], autoPush: true, "sync/shared", [branchSwitch], CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(0, fake.Calls.Count, "An empty sync branch has nothing to publish.");
	}

	[TestMethod]
	public async Task RestoreBranchOnTheOriginalBranchAlreadyDoesNothing()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();
		string originalBranch = repo.CurrentBranch;
		string originalTip = repo.HeadSha;

		await SyncGit.RestoreBranchAsync(new SyncService.BranchSwitch(repo.Root, originalBranch, originalTip)).ConfigureAwait(false);

		Assert.AreEqual(originalBranch, repo.CurrentBranch);
		Assert.AreEqual(originalTip, repo.HeadSha);
	}

	[TestMethod]
	public async Task CommitFilesIsQuietWhenTheSyncedFileIsAlreadyCommitted()
	{
		using TempGitRepo repo = TempGitRepo.WithInitialCommit();

		// Nothing changed since the initial commit, so committing again has an empty tree diff.
		IReadOnlyList<SyncService.BranchSwitch> switches = await SyncService.CommitFilesAsync(
			[Path.Join(repo.Root, "shared.txt")],
			"sync/shared").ConfigureAwait(false);

		Assert.AreEqual(1, switches.Count);
		Assert.AreEqual(
			switches[0].OriginalTipSha,
			repo.HeadSha,
			"An empty commit must be swallowed, not recorded.");
	}

	[TestMethod]
	public async Task RunAsyncWithABranchTouchesNothingWhenEveryCopyIsAlreadyInSync()
	{
		using TempWorkspace workspace = TempWorkspace.WithIdenticalFileInRepos("shared.txt", "same content", "repo-a", "repo-b");
		RecordingProcessService fake = new();

		int exit = await new SyncService(fake)
			.RunAsync(workspace.Root, ["shared.txt"], autoPush: true, "sync/shared", CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		Assert.AreEqual(0, fake.Calls.Count, "Nothing was committed, so there is nothing to push.");
		foreach (string repoRoot in workspace.RepoRoots)
		{
			Assert.AreNotEqual(
				"sync/shared",
				TempGitRepo.BranchOf(repoRoot),
				"A run that commits nothing must leave every repo on its own branch.");
		}
	}

	[TestMethod]
	public async Task RunAsyncRejectsPullRequestsWithoutABranchBeforeScanningAnything()
	{
		using TempWorkspace workspace = TempWorkspace.WithIdenticalFileInRepos("shared.txt", "same content", "repo-a");
		RecordingProcessService fake = new();

		int exit = await new SyncService(fake)
			.RunAsync(workspace.Root, ["shared.txt"], autoPush: true, branch: string.Empty, openPullRequest: true, CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(1, exit, "--pr has nothing to open a pull request from without --branch.");
		Assert.AreEqual(0, fake.Calls.Count, "The combination is rejected before the workspace is walked.");
	}

	[TestMethod]
	public async Task RunAsyncWithPullRequestsTouchesNothingWhenEveryCopyIsAlreadyInSync()
	{
		using TempWorkspace workspace = TempWorkspace.WithIdenticalFileInRepos("shared.txt", "same content", "repo-a", "repo-b");
		RecordingProcessService fake = new();

		int exit = await new SyncService(fake)
			.RunAsync(workspace.Root, ["shared.txt"], autoPush: true, "sync/shared", openPullRequest: true, CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		Assert.AreEqual(0, fake.Calls.Count, "Nothing was pushed, so there is no pull request to open and nothing to run.");
	}

	[TestMethod]
	public async Task RunAsyncWithoutABranchReportsAMissingPath()
	{
		string missing = Path.Join(Path.GetTempPath(), $"ktsu_sync_absent_{Guid.NewGuid():N}");
		AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(missing);

		int exit = await new SyncService(new RecordingProcessService())
			.RunAsync(path, ["shared.txt"], autoPush: false, CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(1, exit);
	}

	[TestMethod]
	public void FindMatchingFilesScansEveryRootNotJustTheFirst()
	{
		using TempTree tree = TempTree.New();
		string first = tree.WriteFile(Path.Join("ktsu-dev", "repo-a"), "shared.txt");
		string second = tree.WriteFile(Path.Join("3k", "repo-b"), "shared.txt");

		IReadOnlyList<string> matches = SyncService.FindMatchingFiles(
			[Path.GetDirectoryName(first)!, Path.GetDirectoryName(second)!],
			["shared.txt"],
			[]);

		CollectionAssert.AreEquivalent(
			new[] { first, second },
			matches.ToArray(),
			"Repos spread across unrelated parents must all be scanned.");
	}

	[TestMethod]
	public void FindMatchingFilesReportsAFileOnceWhenRootsOverlap()
	{
		using TempTree tree = TempTree.New();
		string file = tree.WriteFile(Path.Join("ktsu-dev", "repo-a"), "shared.txt");

		IReadOnlyList<string> matches = SyncService.FindMatchingFiles(
			[tree.Root, Path.GetDirectoryName(file)!],
			["shared.txt"],
			[]);

		Assert.AreEqual(1, matches.Count, "A workspace root and a repo inside it must not double-count.");
		Assert.AreEqual(file, matches[0]);
	}

	[TestMethod]
	public void FindMatchingFilesSkipsAnExcludedDirectoryName()
	{
		using TempTree tree = TempTree.New();
		string kept = tree.WriteFile("repo-a", "shared.txt");
		_ = tree.WriteFile(Path.Join("third-party", "vendored"), "shared.txt");

		IReadOnlyList<string> matches = SyncService.FindMatchingFiles([tree.Root], ["shared.txt"], ["third-party"]);

		Assert.AreEqual(1, matches.Count, "A bare name must exclude the whole subtree beneath it.");
		Assert.AreEqual(kept, matches[0]);
	}

	[TestMethod]
	public void FindMatchingFilesExcludesOnlyTheNamedDirectoryWhenGivenAPath()
	{
		using TempTree tree = TempTree.New();
		string excluded = tree.WriteFile(Path.Join("one", "vendor"), "shared.txt");
		string kept = tree.WriteFile(Path.Join("two", "vendor"), "shared.txt");

		IReadOnlyList<string> matches = SyncService.FindMatchingFiles(
			[tree.Root],
			["shared.txt"],
			[Path.GetDirectoryName(excluded)!]);

		Assert.AreEqual(1, matches.Count, "An exclusion naming a path must not match a same-named directory elsewhere.");
		Assert.AreEqual(kept, matches[0]);
	}

	[TestMethod]
	public void IsExcludedIgnoresATrailingSeparatorOnABareName()
	{
		string file = Path.Join(Path.GetTempPath(), "workspace", "node_modules", "pkg", "shared.txt");

		Assert.IsTrue(SyncService.IsExcluded(file, SyncService.NormalizeExclusions([$"node_modules{Path.DirectorySeparatorChar}"])));
	}

	[TestMethod]
	public void IsExcludedDoesNotMatchTheFileNameItself()
	{
		string file = Path.Join(Path.GetTempPath(), "workspace", "repo-a", "shared.txt");

		Assert.IsFalse(SyncService.IsExcluded(file, SyncService.NormalizeExclusions(["shared.txt"])));
	}

	[TestMethod]
	public void NormalizeExclusionsSplitsCommaSeparatedEntriesAndDropsBlanks()
	{
		IReadOnlyList<string> exclusions = SyncService.NormalizeExclusions(["bin, obj", "  ", "bin"]);

		Assert.AreEqual(2, exclusions.Count);
		Assert.AreEqual("bin", exclusions[0]);
		Assert.AreEqual("obj", exclusions[1]);
	}

	[TestMethod]
	public void ResolveRootsCombinesTheWorkspaceWithExplicitReposWithoutDuplicates()
	{
		using TempTree tree = TempTree.New();
		string repo = tree.CreateDirectory("repo-a");

		IReadOnlyList<string> roots = SyncService.ResolveRoots(tree.Root, [repo, $"{repo}{Path.DirectorySeparatorChar}"], repoListFile: null);

		CollectionAssert.AreEqual(new[] { tree.Root, repo }, roots.ToArray());
	}

	[TestMethod]
	public void ResolveRootsSplitsACommaSeparatedRepoList()
	{
		using TempTree tree = TempTree.New();
		string first = tree.CreateDirectory("repo-a");
		string second = tree.CreateDirectory("repo-b");

		IReadOnlyList<string> roots = SyncService.ResolveRoots(path: null, [$"{first},{second}"], repoListFile: null);

		CollectionAssert.AreEqual(new[] { first, second }, roots.ToArray());
	}

	[TestMethod]
	public void ReadRepoListSkipsBlankLinesAndCommentsAndResolvesRelativeEntries()
	{
		using TempTree tree = TempTree.New();
		string first = tree.CreateDirectory("repo-a");
		string second = tree.CreateDirectory("repo-b");
		string listFile = Path.Join(tree.Root, "repos.txt");
		File.WriteAllLines(listFile, ["# the ktsu clones", "", "repo-a", $"  {second}  "]);

		IReadOnlyList<string> repos = SyncService.ReadRepoList(listFile);

		CollectionAssert.AreEqual(new[] { first, second }, repos.ToArray());
	}

	[TestMethod]
	public void DisplayPathStaysRelativeWhileOneWorkspaceIsScanned()
	{
		string root = Path.Join(Path.GetTempPath(), "workspace");
		string directory = Path.Join(root, "repo-a", "src");

		Assert.AreEqual(Path.Join("repo-a", "src"), SyncService.DisplayPath(directory, [root]));
		Assert.AreEqual("workspace", SyncService.DisplayPath(root, [root]));
	}

	[TestMethod]
	public void DisplayPathIsAbsoluteOnceSeveralRootsAreScanned()
	{
		string first = Path.Join(Path.GetTempPath(), "ktsu-dev");
		string second = Path.Join(Path.GetTempPath(), "3k");
		string directory = Path.Join(first, "repo-a");

		Assert.AreEqual(
			directory,
			SyncService.DisplayPath(directory, [first, second]),
			"Two roots can share a relative path, so only the absolute one is unambiguous.");
	}

	[TestMethod]
	public async Task RunAsyncReportsAMissingRootEvenWhenAnotherRootExists()
	{
		using TempTree tree = TempTree.New();
		string missing = Path.Join(tree.Root, $"absent_{Guid.NewGuid():N}");

		int exit = await new SyncService(new RecordingProcessService())
			.RunAsync([tree.Root, missing], ["shared.txt"], autoPush: false, branch: string.Empty, exclusions: [], CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(1, exit, "Every root is checked, not just the first.");
	}

	[TestMethod]
	public async Task RunAsyncReportsThatThereIsNothingToScanWithoutARoot()
	{
		int exit = await new SyncService(new RecordingProcessService())
			.RunAsync([], ["shared.txt"], autoPush: false, branch: string.Empty, exclusions: [], CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(1, exit);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task RunAsyncReportsWhenNoFilenamePatternIsGiven()
	{
		using TempTree tree = TempTree.New();
		int exit = 0;

		string output = await ConsoleCapture.CaptureAsync(async () =>
			exit = await new SyncService(new RecordingProcessService())
				.RunAsync([tree.Root], ["   "], autoPush: false, branch: string.Empty, exclusions: [], CancellationToken.None)
				.ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exit);
		StringAssert.Contains(output, "No filename patterns provided", StringComparison.Ordinal);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task RunAsyncNamesEveryRootAndExclusionItScans()
	{
		using TempTree first = TempTree.New();
		using TempTree second = TempTree.New();
		_ = first.WriteFile("repo-a", "shared.txt");
		_ = second.WriteFile("repo-b", "shared.txt");
		RecordingProcessService fake = new();
		int exit = 0;

		string output = await ConsoleCapture.CaptureAsync(async () =>
			exit = await new SyncService(fake)
				.RunAsync([first.Root, second.Root], ["shared.txt"], autoPush: false, branch: string.Empty, ["third-party"], CancellationToken.None)
				.ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		Assert.AreEqual(0, fake.Calls.Count, "Identical copies outside a repo leave nothing to commit or push.");
		StringAssert.Contains(output, first.Root, StringComparison.Ordinal);
		StringAssert.Contains(output, second.Root, StringComparison.Ordinal);
		StringAssert.Contains(output, "third-party", StringComparison.Ordinal);
	}

	[TestMethod]
	public void CalculateOldestModificationDatesTakesTheOldestFileInEachGroup()
	{
		using TempTree tree = TempTree.New();
		string recent = Path.GetDirectoryName(tree.WriteFile("recent", "shared.txt"))!;
		string older = Path.GetDirectoryName(tree.WriteFile("older", "shared.txt"))!;
		string oldest = Path.GetDirectoryName(tree.WriteFile("oldest", "shared.txt"))!;

		DateTime baseTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
		File.SetLastWriteTime(Path.Join(recent, "shared.txt"), baseTime);
		File.SetLastWriteTime(Path.Join(older, "shared.txt"), baseTime.AddDays(-1));
		File.SetLastWriteTime(Path.Join(oldest, "shared.txt"), baseTime.AddDays(-2));

		Dictionary<string, Collection<string>> results = new(StringComparer.Ordinal)
		{
			["AAAA"] = [recent],
			["BBBB"] = [older, oldest],
		};

		Dictionary<string, DateTime> dates = SyncService.CalculateOldestModificationDates(results, "shared.txt");

		Assert.AreEqual(baseTime, dates["AAAA"]);
		Assert.AreEqual(
			baseTime.AddDays(-2),
			dates["BBBB"],
			"A group is dated by its oldest copy, not whichever one was listed first.");
	}

	[TestMethod]
	public void JoiningAFileNameOntoADirectoryKeepsTheDirectory()
	{
		string dir = Path.Join(Path.GetTempPath(), "workspace");
		string rooted = Path.Join(Path.GetTempPath(), "elsewhere", "shared.txt");

		// The sync joins a directory to a file name in several places, and uses Path.Join rather than
		// Path.Combine because Combine returns a rooted second argument on its own and drops the
		// directory — which in SyncFilesToHashAsync would make source and destination the same path
		// and copy a file over itself. These pin what the sync relies on from Join: it produces the
		// path it looks like it should, it keeps the directory whatever the second argument is, and
		// the names the sync derives are never rooted to begin with.
		Assert.AreEqual(
			dir + Path.DirectorySeparatorChar + "shared.txt",
			Path.Join(dir, "shared.txt"),
			"A bare file name joins onto the directory exactly as written.");

		StringAssert.StartsWith(Path.Join(dir, rooted), dir, StringComparison.Ordinal, "Join must never drop the directory.");
		Assert.IsFalse(Path.IsPathRooted(Path.GetFileName(rooted)), "A bare file name is never rooted.");
	}

	[TestMethod]
	public void CalculateOldestModificationDatesIgnoresADirectoryOnTheFilename()
	{
		using TempTree tree = TempTree.New();
		string dir = Path.GetDirectoryName(tree.WriteFile("repo-a", "shared.txt"))!;
		DateTime written = new(2026, 2, 3, 9, 30, 0, DateTimeKind.Local);
		File.SetLastWriteTime(Path.Join(dir, "shared.txt"), written);

		Dictionary<string, Collection<string>> results = new(StringComparer.Ordinal) { ["AAAA"] = [dir] };

		// A rooted name would otherwise make Path.Combine discard the directory silently.
		Dictionary<string, DateTime> dates = SyncService.CalculateOldestModificationDates(
			results,
			Path.Join(Path.GetTempPath(), "shared.txt"));

		Assert.AreEqual(written, dates["AAAA"]);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task DisplayHashGroupsTableListsEveryGroupWithItsDirectories()
	{
		string root = Path.Join(Path.GetTempPath(), "workspace");
		Dictionary<string, Collection<string>> results = new(StringComparer.Ordinal)
		{
			["AAAA"] = [Path.Join(root, "repo-a")],
			["BBBB"] = [Path.Join(root, "repo-b")],
		};
		Dictionary<string, DateTime> dates = new(StringComparer.Ordinal)
		{
			["AAAA"] = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local),
			["BBBB"] = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Local),
		};

		string output = await ConsoleCapture.CaptureAsync(() =>
		{
			SyncService.DisplayHashGroupsTable(results, "shared.txt", dates, [root]);
			return Task.CompletedTask;
		}).ConfigureAwait(false);

		StringAssert.Contains(output, "Differences found for:", StringComparison.Ordinal);
		StringAssert.Contains(output, "AAAA", StringComparison.Ordinal);
		StringAssert.Contains(output, "BBBB", StringComparison.Ordinal);
		StringAssert.Contains(output, "repo-a", StringComparison.Ordinal);
		StringAssert.Contains(output, "repo-b", StringComparison.Ordinal);
		Assert.IsFalse(
			output.Contains(root, StringComparison.Ordinal),
			"One root is being scanned, so directories show relative to it.");
	}

	/// <summary>
	/// A throwaway directory tree, for the scanning paths that need files on disk but no git.
	/// </summary>
	private sealed class TempTree : IDisposable
	{
		private TempTree(string root) => Root = root;

		public string Root { get; }

		public static TempTree New() => new(CreateCanonicalTempDirectory("ktsu_sync_tree"));

		public string CreateDirectory(string relativeDirectory)
		{
			string directory = Path.Join(Root, relativeDirectory);
			Directory.CreateDirectory(directory);
			return directory;
		}

		public string WriteFile(string relativeDirectory, string fileName)
		{
			string filePath = Path.Join(CreateDirectory(relativeDirectory), fileName);
			File.WriteAllText(filePath, "same content");
			return filePath;
		}

		public void Dispose() => DeleteGitTree(Root);
	}

	private sealed class TempWorkspace : IDisposable
	{
		private TempWorkspace(string root, IReadOnlyList<string> repoRoots)
		{
			RootDirectory = root;
			RepoRoots = repoRoots;
		}

		private string RootDirectory { get; }

		public IReadOnlyList<string> RepoRoots { get; }

		public AbsoluteDirectoryPath Root => AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(RootDirectory);

		public static TempWorkspace WithIdenticalFileInRepos(string fileName, string content, params string[] repoNames)
		{
			string root = CreateCanonicalTempDirectory("ktsu_sync_ws");

			List<string> repoRoots = [];
			foreach (string repoRoot in repoNames.Select(name => Path.Join(root, name)))
			{
				Directory.CreateDirectory(repoRoot);
				TestGit.Init(repoRoot);
				File.WriteAllText(Path.Join(repoRoot, fileName), content);
				_ = TestGit.Commit(repoRoot, fileName, $"Add {fileName}", "A Human");

				repoRoots.Add(repoRoot);
			}

			return new TempWorkspace(root, repoRoots);
		}

		public void Dispose() => DeleteGitTree(RootDirectory);
	}

	/// <summary>
	/// Creates a temp directory and returns the path with every symlinked component resolved.
	/// libgit2 canonicalises a repository's working directory, and on macOS the temp directory is
	/// reached through a /var -> /private/var symlink, so an uncanonicalised path is rejected as
	/// being outside the repository it is actually inside.
	/// </summary>
	/// <param name="prefix">Prefix for the generated directory name.</param>
	/// <returns>The canonical path of the created directory.</returns>
	private static string CreateCanonicalTempDirectory(string prefix)
	{
		string created = Path.Join(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
		Directory.CreateDirectory(created);
		return CanonicalPathOf(created).TrimEnd(Path.DirectorySeparatorChar);
	}

	/// <summary>
	/// Resolves a directory path one component at a time, since only a path that is itself a link
	/// resolves and on macOS it is the /var prefix that is the link, not the directory below it.
	/// </summary>
	/// <param name="path">The directory path to resolve.</param>
	/// <returns>The path with every symlinked component replaced by its target.</returns>
	private static string CanonicalPathOf(string path)
	{
		DirectoryInfo directory = new(path);

		// The root ("/" or "C:\") is never a link, and asking its parent for one would not terminate.
		if (directory.Parent is null)
		{
			return directory.FullName;
		}

		string resolved = Path.Join(CanonicalPathOf(directory.Parent.FullName), directory.Name);
		return Directory.ResolveLinkTarget(resolved, returnFinalTarget: true)?.FullName ?? resolved;
	}

	private static void DeleteGitTree(string root)
	{
		try
		{
			// Git marks objects and pack files read-only, which blocks a plain recursive delete.
			foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
			{
				File.SetAttributes(file, FileAttributes.Normal);
			}

			Directory.Delete(root, recursive: true);
		}
		catch (DirectoryNotFoundException)
		{
			// Already gone.
		}
	}

	private sealed class RecordingProcessService(int exitCode = 0) : IProcessService
	{
		public List<(string Command, string Arguments, string? WorkingDirectory)> Calls { get; } = [];

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			RunAsync(command, arguments, workingDirectory, null, ct);

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default)
		{
			Calls.Add((command, arguments, workingDirectory));
			return Task.FromResult(new ProcessResult(exitCode, [], []));
		}
	}

	/// <summary>
	/// A throwaway git repository with real commits, so the branch handling is exercised against
	/// the real git binary rather than a stand-in.
	/// </summary>
	private sealed class TempGitRepo : IDisposable
	{
		private TempGitRepo(string root) => Root = root;

		public string Root { get; }

		public static TempGitRepo WithoutAnyCommit()
		{
			string root = CreateCanonicalTempDirectory("ktsu_sync");
			TestGit.Init(root);
			return new TempGitRepo(root);
		}

		public static TempGitRepo WithInitialCommit()
		{
			TempGitRepo repo = WithoutAnyCommit();
			_ = repo.Commit("shared.txt", "original content", "A Human");
			return repo;
		}

		public string CurrentBranch => BranchOf(Root);

		public static string BranchOf(string repoRoot) => TestGit.CurrentBranch(repoRoot);

		public string HeadSha => TestGit.HeadSha(Root);

		public string TipOf(string branchName) => TestGit.TipOf(Root, branchName);

		public void Write(string fileName, string content) =>
			File.WriteAllText(Path.Join(Root, fileName), content);

		public string Read(string fileName) =>
			File.ReadAllText(Path.Join(Root, fileName));

		public string Commit(string fileName, string content, string author)
		{
			Write(fileName, content);
			return TestGit.Commit(Root, fileName, $"Sync {fileName}", author);
		}

		public void Checkout(string branchName) => TestGit.Checkout(Root, branchName);

		/// <summary>Commits on a branch, creating it first, and leaves the repo where it started.</summary>
		public string CommitOnBranch(string branchName, string fileName, string content, string author)
		{
			string startingBranch = CurrentBranch;

			TestGit.CheckoutNew(Root, branchName);

			string sha = Commit(fileName, content, author);
			Checkout(startingBranch);
			return sha;
		}

		public void Dispose() => DeleteGitTree(Root);
	}
}
