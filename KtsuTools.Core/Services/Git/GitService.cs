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
			GitRepository repo = await OpenAsync(repoPath, ct).ConfigureAwait(false);
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
			GitRepository repo = await OpenAsync(repoPath, ct).ConfigureAwait(false);

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
			GitRepository repo = await OpenAsync(repoPath, ct).ConfigureAwait(false);
			GitResult<GitPushResult> result = await repo.Push()
				.ToRemote(Origin)
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
			GitRepository repo = await OpenAsync(repoPath, ct).ConfigureAwait(false);
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
			GitRepository repo = await OpenAsync(repoPath, ct).ConfigureAwait(false);
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
			GitRepositoryRemotePath source = GitRepositoryRemotePath.Create<GitRepositoryRemotePath>(url.AbsoluteUri);
			AbsoluteDirectoryPath destination = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(targetPath);

			return (await gitClient.Clone(source, destination).TryExecuteAsync(ct).ConfigureAwait(false)).Success;
		}
		catch (GitException)
		{
			return false;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	public async Task<bool> IsRepositoryAsync(string path, CancellationToken ct = default)
	{
		try
		{
			return await gitClient
				.IsRepositoryAsync(AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(path), ct)
				.ConfigureAwait(false);
		}
		catch (GitException)
		{
			return false;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	private Task<GitRepository> OpenAsync(string repoPath, CancellationToken ct) =>
		gitClient.OpenAsync(AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(repoPath), ct);

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
