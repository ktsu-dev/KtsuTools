// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Core.Services.GitHub;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public record GitHubRateLimitInfo(int Remaining, int Limit, DateTimeOffset ResetAt);

public record GitHubWorkflowRun(
	long Id,
	string Name,
	string Status,
	string Conclusion,
	string Branch,
	Uri Url,
	DateTimeOffset CreatedAt,
	DateTimeOffset? CompletedAt);

public record GitHubRepository(
	long Id,
	string Name,
	string FullName,
	Uri Url,
	string DefaultBranch);

public interface IGitHubService
{
	public Task InitializeAsync(string token, CancellationToken ct = default);
	public bool IsAuthenticated { get; }
	public Task<IReadOnlyList<GitHubRepository>> GetRepositoriesAsync(string owner, CancellationToken ct = default);
	public Task<IReadOnlyList<GitHubWorkflowRun>> GetWorkflowRunsAsync(string owner, string repo, CancellationToken ct = default);
	public Task<GitHubRateLimitInfo> GetRateLimitAsync(CancellationToken ct = default);
	public Task<bool> RerunWorkflowAsync(string owner, string repo, long runId, CancellationToken ct = default);
	public Task<bool> CancelWorkflowAsync(string owner, string repo, long runId, CancellationToken ct = default);

	/// <summary>
	/// Finds the open pull request whose head is the given branch.
	/// </summary>
	/// <param name="owner">Repository owner.</param>
	/// <param name="repo">Repository name.</param>
	/// <param name="headBranch">The branch the pull request would be opened from.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The pull request's web address, or <see langword="null"/> when none is open.</returns>
	public Task<Uri?> FindOpenPullRequestAsync(string owner, string repo, string headBranch, CancellationToken ct = default);

	/// <summary>
	/// Opens a pull request from one branch onto another.
	/// </summary>
	/// <param name="owner">Repository owner.</param>
	/// <param name="repo">Repository name.</param>
	/// <param name="headBranch">The branch carrying the changes.</param>
	/// <param name="baseBranch">The branch to merge into.</param>
	/// <param name="title">The pull request title.</param>
	/// <param name="body">The pull request body.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The new pull request's web address, or <see langword="null"/> when GitHub rejected it.</returns>
	public Task<Uri?> CreatePullRequestAsync(string owner, string repo, string headBranch, string baseBranch, string title, string body, CancellationToken ct = default);
}
