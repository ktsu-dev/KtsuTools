// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Sync;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using ktsu.GitIntegration;
using ktsu.Semantics.Paths;

using Spectre.Console;

/// <summary>
/// The git operations sync performs, delegated to ktsu.GitIntegration.
/// </summary>
/// <remarks>
/// Sync used to wrap LibGit2Sharp here and <see cref="KtsuTools.Core.Services.Git.GitService"/>
/// wrapped it again for the repo verbs. Both now go through the same library, which shells out to
/// the <c>git</c> binary — so branch tracking, hooks and config behave the way they do on the
/// command line rather than the way libgit2 reimplements them.
/// </remarks>
internal static class SyncGit
{
	internal const string CommitAuthorName = "KtsuTools";

	private static readonly GitAuthorName SyncAuthorName = GitAuthorName.Create<GitAuthorName>(CommitAuthorName);
	private static readonly GitAuthorEmail SyncAuthorEmail = GitAuthorEmail.Create<GitAuthorEmail>(CommitAuthorName);
	private static readonly GitRefName Head = GitRefName.Create<GitRefName>("HEAD");

	// These helpers are static, so the client they share is too. It holds no per-repository state:
	// every call names the repository it acts on.
	private static readonly GitClient Git =
		new(new SyncIdentityGitProcessRunner(new RunCommandGitProcessRunner(new GitOptions())));

	// Windows paths differ only in case, so two spellings of one directory are the same file there
	// and two different files everywhere else.
	private static readonly StringComparer PathComparer =
		OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

	/// <summary>
	/// The working directory of the repository owning the given path.
	/// </summary>
	/// <param name="path">An absolute path to a file or directory inside a repository.</param>
	/// <returns>The repository working directory, or <see langword="null"/> when there is none.</returns>
	internal static async Task<string?> RepoRootForAsync(string path)
	{
		if (DirectoryOf(path) is not AbsoluteDirectoryPath startingPath)
		{
			return null;
		}

		try
		{
			GitRepository? repo = await Git.DiscoverAsync(startingPath).ConfigureAwait(false);
			return repo?.LocalPath?.ToString();
		}
		catch (GitException)
		{
			return null;
		}
	}

	/// <summary>
	/// Checks out the sync branch in a repository, creating it at the current HEAD when it does not
	/// exist and reusing it when it does, and records what to restore afterwards.
	/// </summary>
	/// <param name="repoRoot">The repository working directory.</param>
	/// <param name="branchName">The branch to commit onto.</param>
	/// <returns>The recorded switch, or <see langword="null"/> when the repository has no commit to branch from.</returns>
	internal static async Task<SyncService.BranchSwitch?> SwitchToSyncBranchAsync(string repoRoot, string branchName)
	{
		GitRepository repo = await OpenAsync(repoRoot).ConfigureAwait(false);

		// An unborn HEAD has no tip to branch from, and rev-parse fails rather than answering.
		GitResult<GitCommitSha> tip = await repo.RevParse(Head).TryExecuteAsync().ConfigureAwait(false);
		if (tip is not { Success: true, Value: not null })
		{
			return null;
		}

		string originalTipSha = tip.Value.ToString();

		GitStatus status = await repo.Status().ExecuteAsync().ConfigureAwait(false);
		string currentBranch = CurrentBranchOf(status);
		string original = status.IsDetached ? originalTipSha : currentBranch;

		if (!string.Equals(currentBranch, branchName, StringComparison.Ordinal))
		{
			// Creating it fails when it already exists, which is the "reuse it" case rather than an
			// error, so the checkout that follows is what decides whether the switch worked.
			_ = await repo.CreateBranch(GitBranchName.Create<GitBranchName>(branchName))
				.TryExecuteAsync()
				.ConfigureAwait(false);

			_ = await repo.Checkout(GitRefName.Create<GitRefName>(branchName))
				.ExecuteAsync()
				.ConfigureAwait(false);
		}

		return new SyncService.BranchSwitch(repoRoot, original, originalTipSha);
	}

	/// <summary>
	/// Returns a single repository to the branch it was on before the sync branch was checked out.
	/// </summary>
	/// <param name="branchSwitch">The switch recorded by <see cref="SwitchToSyncBranchAsync"/>.</param>
	/// <returns>A task that completes once the checkout has been restored.</returns>
	internal static async Task RestoreBranchAsync(SyncService.BranchSwitch branchSwitch)
	{
		Ensure.NotNull(branchSwitch);

		GitRepository repo = await OpenAsync(branchSwitch.RepoRoot).ConfigureAwait(false);

		GitStatus status = await repo.Status().ExecuteAsync().ConfigureAwait(false);
		if (string.Equals(CurrentBranchOf(status), branchSwitch.OriginalBranch, StringComparison.Ordinal))
		{
			return;
		}

		_ = await repo.Checkout(GitRefName.Create<GitRefName>(branchSwitch.OriginalBranch))
			.ExecuteAsync()
			.ConfigureAwait(false);
	}

	/// <summary>
	/// Commits on the repository's current HEAD that are not reachable from the recorded base tip,
	/// which for a sync branch is exactly the commits the sync added.
	/// </summary>
	/// <param name="repoRoot">The repository working directory.</param>
	/// <param name="baseTipSha">The tip the sync branch was based on.</param>
	/// <returns>The author name of each commit added on top of the base, newest first.</returns>
	internal static async Task<IReadOnlyList<string>> CommitAuthorsSinceBaseAsync(string repoRoot, string baseTipSha)
	{
		GitRepository repo = await OpenAsync(repoRoot).ConfigureAwait(false);

		// "base..HEAD" is what excluding the base tip means to git; with no base recorded there is
		// nothing to exclude and the whole of HEAD is the answer.
		GitRefName revision = GitRefName.Create<GitRefName>(
			string.IsNullOrEmpty(baseTipSha) ? "HEAD" : $"{baseTipSha}..HEAD");

		GitResult<IReadOnlyList<GitCommit>> commits =
			await repo.Log().ForRevision(revision).TryExecuteAsync().ConfigureAwait(false);

		return commits is { Success: true, Value: not null }
			? [.. commits.Value.Select(c => c.Author.Name.ToString())]
			: [];
	}

	/// <summary>
	/// The files among the sync candidates that have outstanding changes in their repository.
	/// </summary>
	/// <param name="commitDirectories">Directories holding the copied files.</param>
	/// <param name="expandedFilesToSync">File names the sync copied.</param>
	/// <returns>Absolute paths of the files with changes to commit.</returns>
	internal static async Task<Collection<string>> FindChangedFilesAsync(
		HashSet<string> commitDirectories,
		HashSet<string> expandedFilesToSync)
	{
		Ensure.NotNull(commitDirectories);
		Ensure.NotNull(expandedFilesToSync);

		Collection<string> commitFiles = [];

		foreach (string directoryPath in commitDirectories)
		{
			if (await RepoRootForAsync(directoryPath).ConfigureAwait(false) is not string repoRoot)
			{
				continue;
			}

			GitRepository repo = await OpenAsync(repoRoot).ConfigureAwait(false);
			GitResult<GitStatus> status = await repo.Status()
				.WithUntrackedFiles(GitUntrackedFilesMode.All)
				.TryExecuteAsync()
				.ConfigureAwait(false);

			if (status is not { Success: true, Value: not null })
			{
				continue;
			}

			// Status reports paths relative to the repository root, so the candidates are matched
			// against one listing rather than by asking git about each file in turn.
			HashSet<string> changed = new(
				status.Value.Entries
					.Where(e => e.WorkTreeState is GitFileState.Modified or GitFileState.Untracked)
					.Select(e => Normalize(Path.Join(repoRoot, e.Path.ToString()))),
				PathComparer);

			foreach (string filePath in expandedFilesToSync.Select(name => Path.Join(directoryPath, name)))
			{
				if (changed.Contains(Normalize(filePath)))
				{
					commitFiles.Add(filePath);
					AnsiConsole.MarkupLine($"[yellow]{filePath.EscapeMarkup()}[/] has outstanding changes");
				}
			}
		}

		return commitFiles;
	}

	/// <summary>
	/// Stages one file and commits it, attributed to the sync rather than to whoever ran it.
	/// </summary>
	/// <param name="filePath">Absolute path of the file to commit.</param>
	/// <returns>A task that completes once the commit has been attempted.</returns>
	internal static async Task CommitFileAsync(string filePath)
	{
		AnsiConsole.MarkupLine($"[green]Committing:[/] {filePath.EscapeMarkup()}");

		if (await RepoRootForAsync(filePath).ConfigureAwait(false) is not string repoRoot)
		{
			return;
		}

		GitRepository repo = await OpenAsync(repoRoot).ConfigureAwait(false);
		string relativeFilePath = filePath.Replace(repoRoot, "", StringComparison.Ordinal);
		RelativeFilePath stagedPath = RelativeFilePath.Create<RelativeFilePath>(relativeFilePath.TrimStart('/', '\\'));

		if (!(await repo.Add().ForPath(stagedPath).TryExecuteAsync().ConfigureAwait(false)).Success)
		{
			AnsiConsole.MarkupLine($"[red]Could not stage:[/] {filePath.EscapeMarkup()}");
			return;
		}

		GitCommitMessage message = GitCommitMessage.Create<GitCommitMessage>($"Sync {relativeFilePath}");

		// The author is fixed because FindPushableBranchDirectories tells sync's own commits from a
		// person's by exactly this name; taking the local config's signature would break that.
		GitResult<GitCommit> commit = await repo.Commit(message)
			.WithAuthor(SyncAuthorName, SyncAuthorEmail)
			.TryExecuteAsync()
			.ConfigureAwait(false);

		if (commit.Success)
		{
			return;
		}

		// Nothing staged is the ordinary case for a file that matched but did not differ, and git
		// says so on stderr rather than through a distinct exit code.
		string error = commit.Error?.StandardError ?? string.Empty;
		if (error.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase)
			|| error.Contains("nothing added to commit", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		AnsiConsole.MarkupLine($"[red]Could not commit:[/] {filePath.EscapeMarkup()}");
		if (!string.IsNullOrWhiteSpace(error))
		{
			AnsiConsole.MarkupLine($"[red]{error.EscapeMarkup()}[/]");
		}
	}

	/// <summary>
	/// The repositories holding commits that are ahead of their upstream and were all written by
	/// the sync, which are the ones safe to push without asking.
	/// </summary>
	/// <param name="commitDirectories">Directories holding the copied files.</param>
	/// <returns>The repository working directories that can be pushed.</returns>
	internal static async Task<Collection<string>> FindPushableDirectoriesAsync(HashSet<string> commitDirectories)
	{
		Ensure.NotNull(commitDirectories);

		Collection<string> pushDirectories = [];

		foreach (string repoRoot in await RepoRootsForAsync(commitDirectories).ConfigureAwait(false))
		{
			GitRepository repo = await OpenAsync(repoRoot).ConfigureAwait(false);

			GitResult<GitStatus> status = await repo.Status().TryExecuteAsync().ConfigureAwait(false);
			int aheadBy = status is { Success: true, Value: not null } ? status.Value.Ahead : 0;

			if (aheadBy == 0)
			{
				continue;
			}

			GitResult<IReadOnlyList<GitCommit>> commits =
				await repo.Log().Take(aheadBy).TryExecuteAsync().ConfigureAwait(false);

			bool canPush = commits is { Success: true, Value: not null }
				&& commits.Value.All(commit => commit.Author.Name.ToString() == CommitAuthorName);

			if (canPush)
			{
				pushDirectories.Add(repoRoot);
				AnsiConsole.MarkupLine($"[cyan]{repoRoot.EscapeMarkup()}[/] can be pushed automatically");
			}
		}

		return pushDirectories;
	}

	/// <summary>
	/// Distinct repository roots owning the supplied paths, in first-seen order.
	/// </summary>
	/// <param name="paths">Absolute paths of files or directories to resolve.</param>
	/// <returns>The repository working directories, without duplicates.</returns>
	internal static async Task<IReadOnlyList<string>> RepoRootsForAsync(IEnumerable<string> paths)
	{
		Ensure.NotNull(paths);

		List<string> roots = [];

		foreach (string path in paths)
		{
			if (await RepoRootForAsync(path).ConfigureAwait(false) is string root
				&& !roots.Contains(root, StringComparer.Ordinal))
			{
				roots.Add(root);
			}
		}

		return roots;
	}

	/// <summary>
	/// Reads the push URL of a repository's named remote.
	/// </summary>
	/// <param name="repoRoot">The repository working directory.</param>
	/// <param name="remoteName">The remote to read.</param>
	/// <returns>The remote URL, or <see langword="null"/> when there is no such remote.</returns>
	internal static async Task<string?> RemoteUrlOfAsync(string repoRoot, string remoteName)
	{
		try
		{
			GitRepository repo = await OpenAsync(repoRoot).ConfigureAwait(false);

			GitResult<IReadOnlyList<GitRemote>> remotes =
				await repo.Remotes().TryExecuteAsync().ConfigureAwait(false);

			if (remotes is not { Success: true, Value: not null })
			{
				return null;
			}

			GitRemote? remote = remotes.Value
				.FirstOrDefault(r => string.Equals(r.Name.ToString(), remoteName, StringComparison.Ordinal));

			return remote?.PushUrl?.ToString();
		}
		catch (GitException)
		{
			return null;
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	/// <summary>
	/// The branch a status is on, or empty when HEAD is detached.
	/// </summary>
	/// <param name="status">The status to read.</param>
	/// <returns>The branch name, or an empty string.</returns>
	private static string CurrentBranchOf(GitStatus status) =>
		status.IsDetached ? string.Empty : status.Branch?.ToString() ?? string.Empty;

	/// <summary>
	/// Opens the repository at the given working directory.
	/// </summary>
	/// <param name="repoRoot">The repository working directory.</param>
	/// <returns>The opened repository.</returns>
	private static Task<GitRepository> OpenAsync(string repoRoot) =>
		Git.OpenAsync(AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(repoRoot));

	/// <summary>
	/// The directory a discovery should start from for the given path, which is the path itself when
	/// it names a directory and its parent when it names a file.
	/// </summary>
	/// <param name="path">The absolute path to resolve.</param>
	/// <returns>The directory, or <see langword="null"/> when the path cannot be read as one.</returns>
	private static AbsoluteDirectoryPath? DirectoryOf(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return null;
		}

		try
		{
			return Directory.Exists(path)
				? AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(path)
				: AbsoluteFilePath.Create<AbsoluteFilePath>(path).AbsoluteDirectoryPath;
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	/// <summary>
	/// Puts a path into one spelling so two references to one file compare equal, whichever
	/// separator each was written with.
	/// </summary>
	/// <param name="path">The path to normalize.</param>
	/// <returns>The normalized path.</returns>
	private static string Normalize(string path) =>
		path.Replace('\\', '/').TrimEnd('/');

	/// <summary>
	/// Runs git with the sync's identity supplied on the command line.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>--author</c> names who wrote a commit, but git still needs a <em>committer</em>, and it
	/// takes that from configuration rather than from any commit option. On a machine with no
	/// <c>user.name</c> set — a CI runner, most often — <c>git commit</c> refuses outright with
	/// "empty ident name ... not allowed", so the sync could not commit at all.
	/// </para>
	/// <para>
	/// The LibGit2Sharp implementation this replaced passed one explicit signature as both author
	/// and committer, so it never read configuration and never depended on the machine having an
	/// identity. Supplying both here through <c>-c</c> keeps that behaviour, and does it without
	/// writing to the user's config or to the environment — the repositories sync operates on
	/// belong to someone else, and mutating either would outlive the run.
	/// </para>
	/// </remarks>
	/// <param name="inner">The runner that actually starts git.</param>
	private sealed class SyncIdentityGitProcessRunner(IGitProcessRunner inner) : IGitProcessRunner
	{
		private static readonly string[] Identity =
		[
			"-c", $"user.name={CommitAuthorName}",
			"-c", $"user.email={CommitAuthorName}",
		];

		public Task<GitProcessResult> RunAsync(GitProcessRequest request, CancellationToken cancellationToken)
		{
			Ensure.NotNull(request);

			return inner.RunAsync(
				request with { Arguments = [.. Identity, .. request.Arguments] },
				cancellationToken);
		}
	}
}
