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
	private IGitHubClient? _client;

	/// <summary>
	/// Initializes a new instance of the <see cref="GitHubService"/> class. Call
	/// <see cref="InitializeAsync"/> before using it.
	/// </summary>
	public GitHubService()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="GitHubService"/> class over an existing client.
	/// </summary>
	/// <param name="client">The client to make requests with.</param>
	internal GitHubService(IGitHubClient client) => _client = client;

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
		IGitHubClient client = GetClient();

		// /users/{owner}/repos lists only an organization's public repositories, so an organization
		// is asked through its own endpoint, which includes the private ones the token can see.
		User account = await client.User.Get(owner).ConfigureAwait(false);
		IReadOnlyList<Repository> repos = account.Type == AccountType.Organization
			? await client.Repository.GetAllForOrg(owner).ConfigureAwait(false)
			: await client.Repository.GetAllForUser(owner).ConfigureAwait(false);
		return
		[
			.. repos.Select(r => new GitHubRepository(r.Id, r.Name, r.FullName, new Uri(r.HtmlUrl), r.DefaultBranch)),
		];
	}

	public async Task<IReadOnlyList<GitHubWorkflowRun>> GetWorkflowRunsAsync(string owner, string repo, CancellationToken ct = default)
	{
		IGitHubClient client = GetClient();
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
				StartedAtOf(r.CreatedAt, r.RunStartedAt),
				Math.Max(r.RunAttempt, 1),
				CompletedAtOf(r.Status, r.UpdatedAt))),
		];
	}

	/// <summary>
	/// When a run finished. GitHub has no completion time on a run, so a completed run's last update
	/// stands in for it. A queued or running run has not completed, whatever its update time says.
	/// </summary>
	/// <param name="status">The run's status.</param>
	/// <param name="updatedAt">When the run was last updated.</param>
	/// <returns>The completion time, or <see langword="null"/> when the run has not completed.</returns>
	internal static DateTimeOffset? CompletedAtOf(StringEnum<WorkflowRunStatus> status, DateTimeOffset updatedAt) =>
		string.Equals(status.StringValue, "completed", StringComparison.OrdinalIgnoreCase) ? updatedAt : null;

	/// <summary>
	/// When a run's latest attempt started. A re-run keeps the run's id and creation time and resets
	/// only <c>run_started_at</c>, so that is the start of the attempt being shown. A response without
	/// it (from an older GitHub Enterprise Server) falls back to the creation time.
	/// </summary>
	/// <param name="createdAt">When the run was created.</param>
	/// <param name="runStartedAt">When the latest attempt started, or the default value when not reported.</param>
	/// <returns>The start of the latest attempt.</returns>
	internal static DateTimeOffset StartedAtOf(DateTimeOffset createdAt, DateTimeOffset runStartedAt) =>
		runStartedAt == default ? createdAt : runStartedAt;

	public async Task<GitHubRateLimitInfo> GetRateLimitAsync(CancellationToken ct = default)
	{
		IGitHubClient client = GetClient();
		MiscellaneousRateLimit rateLimit = await client.RateLimit.GetRateLimits().ConfigureAwait(false);
		RateLimit core = rateLimit.Resources.Core;
		return new GitHubRateLimitInfo(core.Remaining, core.Limit, core.Reset);
	}

	public async Task<bool> RerunWorkflowAsync(string owner, string repo, long runId, CancellationToken ct = default)
	{
		try
		{
			IGitHubClient client = GetClient();
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
			IGitHubClient client = GetClient();
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
			IGitHubClient client = GetClient();

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
			IGitHubClient client = GetClient();
			NewPullRequest request = new(title, headBranch, baseBranch) { Body = body };
			PullRequest created = await client.PullRequest.Create(owner, repo, request).ConfigureAwait(false);
			return new Uri(created.HtmlUrl);
		}
		catch (ApiException)
		{
			return null;
		}
	}

	private IGitHubClient GetClient() =>
		_client ?? throw new InvalidOperationException("GitHub client not initialized. Call InitializeAsync first.");
}
