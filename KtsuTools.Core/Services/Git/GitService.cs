// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Core.Services.Git;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ktsu.GitIntegration;
using ktsu.Semantics.Paths;

/// <summary>
/// Local git operations, delegated to ktsu.GitIntegration.
/// </summary>
/// <remarks>
/// Every method answers <see langword="false"/> (or an empty result) rather than throwing, which is
/// the contract callers were written against. Failures arrive two ways and both are handled:
/// a command that runs and exits non-zero comes back as an unsuccessful
/// <see cref="GitResult{T}"/> from <c>TryExecuteAsync</c>, while a path that is not a repository at
/// all throws <see cref="GitException"/> out of the open.
/// </remarks>
/// <param name="gitClient">The git client used to open repositories.</param>
public class GitService(IGitClient gitClient) : IGitService
{
	private readonly IGitClient gitClient = gitClient;

	private static readonly GitRemoteName Origin = GitRemoteName.Create<GitRemoteName>("origin");

	/// <summary>
	/// Initializes a service over the real <c>git</c> binary.
	/// </summary>
	public GitService()
		: this(new GitClient(new RunCommandGitProcessRunner(new GitOptions())))
	{
	}

	public async Task<bool> PullAsync(string repoPath, CancellationToken ct = default)
	{
		try
		{
			if (await OpenOrNullAsync(repoPath, ct).ConfigureAwait(false) is not GitRepository repo)
			{
				return false;
			}

			return (await repo.Pull().TryExecuteAsync(ct).ConfigureAwait(false)).Success;
		}
		catch (GitException)
		{
			return false;
		}
	}

	public async Task<bool> CommitAsync(string repoPath, string message, CancellationToken ct = default)
	{
		try
		{
			if (await OpenOrNullAsync(repoPath, ct).ConfigureAwait(false) is not GitRepository repo)
			{
				return false;
			}

			if (!(await repo.Add().All().TryExecuteAsync(ct).ConfigureAwait(false)).Success)
			{
				return false;
			}

			GitCommitMessage commitMessage = GitCommitMessage.Create<GitCommitMessage>(message);
			return (await repo.Commit(commitMessage).TryExecuteAsync(ct).ConfigureAwait(false)).Success;
		}
		catch (GitException)
		{
			return false;
		}
	}

	public async Task<bool> PushAsync(string repoPath, CancellationToken ct = default)
	{
		try
		{
			if (await OpenOrNullAsync(repoPath, ct).ConfigureAwait(false) is not GitRepository repo)
			{
				return false;
			}

			GitResult<GitStatus> status = await repo.Status().TryExecuteAsync(ct).ConfigureAwait(false);
			if (status is not { Success: true, Value.IsDetached: false } || status.Value.Branch is null)
			{
				return false;
			}

			// Naming the branch is what makes the first push of a new one work. A bare
			// "git push origin" refuses a branch with no upstream — "the current branch has no
			// upstream branch", exit 128 — whereas the libgit2 implementation this replaced pushed
			// the ref explicitly and never consulted tracking configuration.
			GitResult<GitPushResult> result = await repo.Push()
				.ToRemote(Origin)
				.WithBranch(status.Value.Branch)
				.TryExecuteAsync(ct)
				.ConfigureAwait(false);

			// git can exit zero while still refusing an individual ref, so the rejection flag is
			// part of the answer rather than something the exit code already covered.
			return result is { Success: true, Value: not null } && !result.Value.HasRejections;
		}
		catch (GitException)
		{
			return false;
		}
	}

	public async Task<IReadOnlyList<string>> GetStatusAsync(string repoPath, CancellationToken ct = default)
	{
		try
		{
			if (await OpenOrNullAsync(repoPath, ct).ConfigureAwait(false) is not GitRepository repo)
			{
				return [];
			}

			GitResult<GitStatus> result = await repo.Status().TryExecuteAsync(ct).ConfigureAwait(false);

			return result is { Success: true, Value: not null }
				? [.. result.Value.Entries.Select(Describe)]
				: [];
		}
		catch (GitException)
		{
			return [];
		}
	}

	public async Task<string> GetCurrentBranchAsync(string repoPath, CancellationToken ct = default)
	{
		try
		{
			if (await OpenOrNullAsync(repoPath, ct).ConfigureAwait(false) is not GitRepository repo)
			{
				return string.Empty;
			}

			GitResult<GitStatus> result = await repo.Status().TryExecuteAsync(ct).ConfigureAwait(false);

			return result is { Success: true, Value.IsDetached: false }
				? result.Value.Branch?.ToString() ?? string.Empty
				: string.Empty;
		}
		catch (GitException)
		{
			return string.Empty;
		}
	}

	public async Task<bool> CloneAsync(Uri url, string targetPath, CancellationToken ct = default)
	{
		Ensure.NotNull(url);

		try
		{
			if (DirectoryOrNull(targetPath) is not AbsoluteDirectoryPath destination)
			{
				return false;
			}

			GitRepositoryRemotePath source = GitRepositoryRemotePath.Create<GitRepositoryRemotePath>(url.AbsoluteUri);

			return (await gitClient.Clone(source, destination).TryExecuteAsync(ct).ConfigureAwait(false)).Success;
		}
		catch (GitException)
		{
			return false;
		}
	}

	public async Task<bool> IsRepositoryAsync(string path, CancellationToken ct = default)
	{
		try
		{
			return DirectoryOrNull(path) is AbsoluteDirectoryPath directory
				&& await gitClient.IsRepositoryAsync(directory, ct).ConfigureAwait(false);
		}
		catch (GitException)
		{
			return false;
		}
	}

	/// <summary>
	/// Opens the repository at a path, or answers <see langword="null"/> when the path cannot name
	/// one.
	/// </summary>
	/// <param name="repoPath">The repository working directory.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The opened repository, or <see langword="null"/>.</returns>
	private async Task<GitRepository?> OpenOrNullAsync(string repoPath, CancellationToken ct) =>
		DirectoryOrNull(repoPath) is AbsoluteDirectoryPath directory
			? await gitClient.OpenAsync(directory, ct).ConfigureAwait(false)
			: null;

	/// <summary>
	/// Reads a string as an absolute directory, or answers <see langword="null"/> when it is not
	/// one.
	/// </summary>
	/// <remarks>
	/// A relative or otherwise malformed path is refused by <c>AbsoluteDirectoryPath</c> with an
	/// <see cref="ArgumentException"/>, which is not a <see cref="GitException"/> and so would
	/// otherwise leave this class by a route its callers do not expect. They were written against
	/// an implementation that answered for any string it was handed.
	/// </remarks>
	/// <param name="path">The path to read.</param>
	/// <returns>The directory, or <see langword="null"/>.</returns>
	private static AbsoluteDirectoryPath? DirectoryOrNull(string path)
	{
		try
		{
			return AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(path);
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	/// <summary>
	/// Renders one status entry as "state: path", the shape callers already display.
	/// </summary>
	/// <param name="entry">The entry to describe.</param>
	/// <returns>The single-line description.</returns>
	private static string Describe(GitStatusEntry entry)
	{
		// A file staged and then edited again carries a state in both columns; the worktree one is
		// what the caller is being told about, so it wins when the two disagree.
		GitFileState state = entry.WorkTreeState is not GitFileState.Unmodified
			? entry.WorkTreeState
			: entry.IndexState;

		return $"{state}: {entry.Path}";
	}
}
