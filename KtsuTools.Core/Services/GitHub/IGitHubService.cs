// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

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
}
