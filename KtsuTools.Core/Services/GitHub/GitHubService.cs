// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Core.Services.GitHub;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Octokit;

public class GitHubService : IGitHubService
{
	private GitHubClient? _client;

	public bool IsAuthenticated => _client is not null;

	public Task InitializeAsync(string token, CancellationToken ct = default)
	{
		_client = new GitHubClient(new ProductHeaderValue("KtsuTools"))
		{
			Credentials = new Credentials(token),
		};
		return Task.CompletedTask;
	}

	public async Task<IReadOnlyList<GitHubRepository>> GetRepositoriesAsync(string owner, CancellationToken ct = default)
	{
		GitHubClient client = GetClient();
		IReadOnlyList<Repository> repos = await client.Repository.GetAllForUser(owner).ConfigureAwait(false);
		return
		[
			.. repos.Select(r => new GitHubRepository(r.Id, r.Name, r.FullName, new Uri(r.HtmlUrl), r.DefaultBranch)),
		];
	}

	public async Task<IReadOnlyList<GitHubWorkflowRun>> GetWorkflowRunsAsync(string owner, string repo, CancellationToken ct = default)
	{
		GitHubClient client = GetClient();
		WorkflowRunsResponse runs = await client.Actions.Workflows.Runs.List(owner, repo).ConfigureAwait(false);
		return
		[
			.. runs.WorkflowRuns.Select(r => new GitHubWorkflowRun(
				r.Id,
				r.Name,
				r.Status.StringValue ?? "unknown",
				r.Conclusion?.StringValue ?? "pending",
				r.HeadBranch,
				new Uri(r.HtmlUrl),
				r.CreatedAt,
				r.UpdatedAt)),
		];
	}

	public async Task<GitHubRateLimitInfo> GetRateLimitAsync(CancellationToken ct = default)
	{
		GitHubClient client = GetClient();
		MiscellaneousRateLimit rateLimit = await client.RateLimit.GetRateLimits().ConfigureAwait(false);
		RateLimit core = rateLimit.Resources.Core;
		return new GitHubRateLimitInfo(core.Remaining, core.Limit, core.Reset);
	}

	public async Task<bool> RerunWorkflowAsync(string owner, string repo, long runId, CancellationToken ct = default)
	{
		try
		{
			GitHubClient client = GetClient();
			await client.Actions.Workflows.Runs.Rerun(owner, repo, runId).ConfigureAwait(false);
			return true;
		}
		catch (ApiException)
		{
			return false;
		}
	}

	public async Task<bool> CancelWorkflowAsync(string owner, string repo, long runId, CancellationToken ct = default)
	{
		try
		{
			GitHubClient client = GetClient();
			await client.Actions.Workflows.Runs.Cancel(owner, repo, runId).ConfigureAwait(false);
			return true;
		}
		catch (ApiException)
		{
			return false;
		}
	}

	private GitHubClient GetClient() =>
		_client ?? throw new InvalidOperationException("GitHub client not initialized. Call InitializeAsync first.");
}
