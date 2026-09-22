// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using KtsuTools.Core.Services.Git;

/// <summary>
/// Drives <see cref="GitService"/> against real repositories on disk.
/// </summary>
/// <remarks>
/// The service had no behavioural tests while it wrapped LibGit2Sharp; these pin what each verb
/// does now that it delegates to ktsu.GitIntegration, including the two contracts that are easy to
/// lose in a migration — that a failure is reported as <see langword="false"/> rather than thrown,
/// and that a path which is not a repository is one of those failures rather than a crash.
/// </remarks>
[TestClass]
public class GitServiceTests
{
	[TestMethod]
	public async Task IsRepositoryIsTrueForARepositoryAndFalseForAPlainDirectory()
	{
		using TempRepo repo = TempRepo.WithInitialCommit();
		using TempDir plain = TempDir.Empty();
		GitService service = new();

		Assert.IsTrue(await service.IsRepositoryAsync(repo.Root).ConfigureAwait(false));
		Assert.IsFalse(await service.IsRepositoryAsync(plain.Root).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task IsRepositoryIsFalseForAPathThatDoesNotExist()
	{
		GitService service = new();

		string missing = Path.Join(Path.GetTempPath(), $"ktsu_git_missing_{Guid.NewGuid():N}");

		Assert.IsFalse(await service.IsRepositoryAsync(missing).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task GetCurrentBranchNamesTheCheckedOutBranch()
	{
		using TempRepo repo = TempRepo.WithInitialCommit();
		GitService service = new();

		Assert.AreEqual("main", await service.GetCurrentBranchAsync(repo.Root).ConfigureAwait(false));

		TestGit.CheckoutNew(repo.Root, "feature/work");

		Assert.AreEqual("feature/work", await service.GetCurrentBranchAsync(repo.Root).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task GetCurrentBranchIsEmptyForADirectoryThatIsNotARepository()
	{
		using TempDir plain = TempDir.Empty();
		GitService service = new();

		Assert.AreEqual(string.Empty, await service.GetCurrentBranchAsync(plain.Root).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task GetStatusIsEmptyForACleanRepositoryAndNamesAChangedFile()
	{
		using TempRepo repo = TempRepo.WithInitialCommit();
		GitService service = new();

		IReadOnlyList<string> clean = await service.GetStatusAsync(repo.Root).ConfigureAwait(false);
		Assert.AreEqual(0, clean.Count, "A repository with nothing outstanding reports nothing.");

		await File.WriteAllTextAsync(Path.Join(repo.Root, "shared.txt"), "edited").ConfigureAwait(false);

		IReadOnlyList<string> dirty = await service.GetStatusAsync(repo.Root).ConfigureAwait(false);

		Assert.AreEqual(1, dirty.Count);
		Assert.IsTrue(
			dirty[0].EndsWith("shared.txt", StringComparison.Ordinal),
			$"The entry should name the file it is about, but was '{dirty[0]}'.");
		Assert.IsTrue(
			dirty[0].Contains(':', StringComparison.Ordinal),
			$"The entry should carry a state as well as a path, but was '{dirty[0]}'.");
	}

	[TestMethod]
	public async Task GetStatusReportsAnUntrackedFile()
	{
		using TempRepo repo = TempRepo.WithInitialCommit();
		GitService service = new();

		await File.WriteAllTextAsync(Path.Join(repo.Root, "brand-new.txt"), "new").ConfigureAwait(false);

		IReadOnlyList<string> status = await service.GetStatusAsync(repo.Root).ConfigureAwait(false);

		Assert.IsTrue(
			status.Any(entry => entry.EndsWith("brand-new.txt", StringComparison.Ordinal)),
			"A file git has never seen is still something the caller is being told about.");
	}

	[TestMethod]
	public async Task CommitStagesEveryChangeAndAdvancesHead()
	{
		using TempRepo repo = TempRepo.WithInitialCommit();
		GitService service = new();

		string before = TestGit.HeadSha(repo.Root);
		await File.WriteAllTextAsync(Path.Join(repo.Root, "shared.txt"), "edited").ConfigureAwait(false);
		await File.WriteAllTextAsync(Path.Join(repo.Root, "added.txt"), "added").ConfigureAwait(false);

		Assert.IsTrue(await service.CommitAsync(repo.Root, "A message").ConfigureAwait(false));

		Assert.AreNotEqual(before, TestGit.HeadSha(repo.Root), "The commit should have moved HEAD.");
		Assert.AreEqual(0, (await service.GetStatusAsync(repo.Root).ConfigureAwait(false)).Count);
		Assert.AreEqual("A message", TestGit.Run(repo.Root, "log", "-1", "--pretty=%s"));
	}

	[TestMethod]
	public async Task CommitIsRefusedWhenThereIsNothingToCommit()
	{
		using TempRepo repo = TempRepo.WithInitialCommit();
		GitService service = new();

		Assert.IsFalse(
			await service.CommitAsync(repo.Root, "Nothing changed").ConfigureAwait(false),
			"An empty commit is a failure, the same answer the LibGit2Sharp implementation gave.");
	}

	[TestMethod]
	public async Task CommitIsRefusedForADirectoryThatIsNotARepository()
	{
		using TempDir plain = TempDir.Empty();
		GitService service = new();

		Assert.IsFalse(await service.CommitAsync(plain.Root, "A message").ConfigureAwait(false));
	}

	[TestMethod]
	public async Task PullAndPushAreRefusedWhenThereIsNoRemote()
	{
		using TempRepo repo = TempRepo.WithInitialCommit();
		GitService service = new();

		// The contract these callers were written against is an answer, not an exception.
		Assert.IsFalse(await service.PullAsync(repo.Root).ConfigureAwait(false));
		Assert.IsFalse(await service.PushAsync(repo.Root).ConfigureAwait(false));
	}

	/// <summary>
	/// Pushes a branch that has never been pushed, which is the case a bare <c>git push origin</c>
	/// refuses for want of an upstream. The libgit2 implementation this replaced named the ref
	/// explicitly and so never met that refusal; naming the branch keeps it that way.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[TestMethod]
	public async Task PushSendsABranchThatHasNoUpstreamYet()
	{
		using TempDir remote = TempDir.Empty();
		using TempRepo repo = TempRepo.WithInitialCommit();
		GitService service = new();

		TestGit.InitBare(remote.Root);
		TestGit.AddRemote(repo.Root, "origin", remote.Root);

		Assert.IsTrue(
			await service.PushAsync(repo.Root).ConfigureAwait(false),
			"A first push must succeed; the branch has no upstream and that is the ordinary case.");

		Assert.AreEqual(
			TestGit.HeadSha(repo.Root),
			TestGit.Run(remote.Root, "rev-parse", "main"),
			"The remote should carry the same commit, not merely have accepted the command.");
	}

	[TestMethod]
	public async Task PullBringsDownACommitMadeElsewhere()
	{
		using TempDir remote = TempDir.Empty();
		using TempRepo origin = TempRepo.WithInitialCommit();
		using TempDir second = TempDir.Empty();
		GitService service = new();

		TestGit.InitBare(remote.Root);
		TestGit.AddRemote(origin.Root, "origin", remote.Root);
		Assert.IsTrue(await service.PushAsync(origin.Root).ConfigureAwait(false));

		string clone = Path.Join(second.Root, "clone");
		TestGit.Clone(remote.Root, clone);

		await File.WriteAllTextAsync(Path.Join(origin.Root, "shared.txt"), "second revision").ConfigureAwait(false);
		Assert.IsTrue(await service.CommitAsync(origin.Root, "Second").ConfigureAwait(false));
		Assert.IsTrue(await service.PushAsync(origin.Root).ConfigureAwait(false));

		Assert.IsTrue(await service.PullAsync(clone).ConfigureAwait(false));

		Assert.AreEqual(
			"second revision",
			await File.ReadAllTextAsync(Path.Join(clone, "shared.txt")).ConfigureAwait(false),
			"The pull should have brought the other side's commit into the working tree.");
	}

	[TestMethod]
	public async Task GetCurrentBranchIsEmptyWhenHeadIsDetached()
	{
		using TempRepo repo = TempRepo.WithInitialCommit();
		GitService service = new();

		_ = TestGit.Run(repo.Root, "checkout", "--detach");

		Assert.AreEqual(
			string.Empty,
			await service.GetCurrentBranchAsync(repo.Root).ConfigureAwait(false),
			"A detached HEAD is on no branch, so there is no name to report.");
	}

	[TestMethod]
	public async Task CloneCopiesARepositoryAndReportsFailureForOneThatIsNotThere()
	{
		using TempRepo source = TempRepo.WithInitialCommit();
		using TempDir destination = TempDir.Empty();
		GitService service = new();

		string target = Path.Join(destination.Root, "clone");

		Assert.IsTrue(await service.CloneAsync(new Uri(source.Root), target).ConfigureAwait(false));
		Assert.IsTrue(File.Exists(Path.Join(target, "shared.txt")), "The clone should carry the source's content.");
		Assert.IsTrue(await service.IsRepositoryAsync(target).ConfigureAwait(false));

		string missing = Path.Join(Path.GetTempPath(), $"ktsu_git_absent_{Guid.NewGuid():N}");

		Assert.IsFalse(
			await service.CloneAsync(new Uri(missing), Path.Join(destination.Root, "second")).ConfigureAwait(false));
	}

	/// <summary>A temporary directory that is not a git repository.</summary>
	private sealed class TempDir : IDisposable
	{
		private TempDir(string root) => Root = root;

		public string Root { get; }

		public static TempDir Empty()
		{
			string root = Path.Join(Path.GetTempPath(), $"ktsu_git_plain_{Guid.NewGuid():N}");
			_ = Directory.CreateDirectory(root);
			return new TempDir(root);
		}

		public void Dispose() => DeleteTree(Root);
	}

	/// <summary>A temporary git repository, built with the real git binary.</summary>
	private sealed class TempRepo : IDisposable
	{
		private TempRepo(string root) => Root = root;

		public string Root { get; }

		public static TempRepo WithInitialCommit()
		{
			string root = Path.Join(Path.GetTempPath(), $"ktsu_git_{Guid.NewGuid():N}");
			_ = Directory.CreateDirectory(root);
			TestGit.Init(root);
			File.WriteAllText(Path.Join(root, "shared.txt"), "original");
			_ = TestGit.Commit(root, "shared.txt", "Add shared.txt", "A Human");
			return new TempRepo(root);
		}

		public void Dispose() => DeleteTree(Root);
	}

	/// <summary>
	/// Deletes a tree, clearing the read-only bit git sets on objects and pack files first.
	/// </summary>
	/// <param name="root">The directory to delete.</param>
	private static void DeleteTree(string root)
	{
		if (!Directory.Exists(root))
		{
			return;
		}

		foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
		{
			File.SetAttributes(file, FileAttributes.Normal);
		}

		Directory.Delete(root, recursive: true);
	}
}
