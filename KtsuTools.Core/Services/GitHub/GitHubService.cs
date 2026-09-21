// Copyright (c) 2023-2026 ktsu-dev contributors

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

	public async Task<Uri?> FindOpenPullRequestAsync(string owner, string repo, string headBranch, CancellationToken ct = default)
	{
		try
		{
			GitHubClient client = GetClient();

			// The head filter is owner-qualified, which is what tells a branch in this repository
			// apart from a same-named branch on a fork.
			PullRequestRequest request = new()
			{
				State = ItemStateFilter.Open,
				Head = $"{owner}:{headBranch}",
			};

			IReadOnlyList<PullRequest> open = await client.PullRequest.GetAllForRepository(owner, repo, request).ConfigureAwait(false);
			return open.Count == 0 ? null : new Uri(open[0].HtmlUrl);
		}
		catch (ApiException)
		{
			return null;
		}
	}

	public async Task<Uri?> CreatePullRequestAsync(string owner, string repo, string headBranch, string baseBranch, string title, string body, CancellationToken ct = default)
	{
		try
		{
			GitHubClient client = GetClient();
			NewPullRequest request = new(title, headBranch, baseBranch) { Body = body };
			PullRequest created = await client.PullRequest.Create(owner, repo, request).ConfigureAwait(false);
			return new Uri(created.HtmlUrl);
		}
		catch (ApiException)
		{
			return null;
		}
	}

	private GitHubClient GetClient() =>
		_client ?? throw new InvalidOperationException("GitHub client not initialized. Call InitializeAsync first.");
}
