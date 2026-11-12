// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using KtsuTools.BuildMonitor;
using KtsuTools.Core.Services.GitHub;
using Moq;
using Octokit;
using Octokit.Internal;
using Spectre.Console;
using Spectre.Console.Rendering;

[TestClass]
[DoNotParallelize]
public class BuildMonitorServiceTests
{
	private static readonly GitHubRepository RepoA = new(1, "a", "ktsu-dev/a", new Uri("https://github.com/ktsu-dev/a"), "main");
	private static readonly GitHubRepository RepoB = new(2, "b", "ktsu-dev/b", new Uri("https://github.com/ktsu-dev/b"), "main");

	[TestMethod]
	public async Task RenderDashboardShowsARejectedTokenInsteadOfThrowing()
	{
		Mock<IGitHubService> api = AuthenticatedApi();
		api.Setup(s => s.GetRepositoriesAsync("ktsu-dev", It.IsAny<CancellationToken>()))
			.ThrowsAsync(new AuthorizationException(Response(HttpStatusCode.Unauthorized)));

		string output = await RenderAsync(api.Object).ConfigureAwait(false);

		StringAssert.Contains(output, "rejected the token (401)");
	}

	[TestMethod]
	public async Task RenderDashboardShowsAnUnknownOwnerInsteadOfThrowing()
	{
		Mock<IGitHubService> api = AuthenticatedApi();
		api.Setup(s => s.GetRepositoriesAsync("ktsu-dev", It.IsAny<CancellationToken>()))
			.ThrowsAsync(new NotFoundException("Not Found", HttpStatusCode.NotFound));

		string output = await RenderAsync(api.Object).ConfigureAwait(false);

		StringAssert.Contains(output, "API Error (404)");
	}

	[TestMethod]
	public async Task RenderDashboardShowsWhenTheRateLimitResets()
	{
		Mock<IGitHubService> api = AuthenticatedApi();
		api.Setup(s => s.GetRepositoriesAsync("ktsu-dev", It.IsAny<CancellationToken>())).ReturnsAsync([RepoA, RepoB]);
		api.Setup(s => s.GetWorkflowRunsAsync("ktsu-dev", It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new RateLimitExceededException(Response(HttpStatusCode.Forbidden, rateLimited: true)));

		string output = await RenderAsync(api.Object).ConfigureAwait(false);

		StringAssert.Contains(output, "rate limit exceeded (5000 requests/hour)");
		StringAssert.Contains(output, "Resets");
	}

	[TestMethod]
	public async Task RenderDashboardMarksOnlyTheRepositoryWhoseRunsFailed()
	{
		Mock<IGitHubService> api = AuthenticatedApi();
		api.Setup(s => s.GetRepositoriesAsync("ktsu-dev", It.IsAny<CancellationToken>())).ReturnsAsync([RepoA, RepoB]);
		api.Setup(s => s.GetWorkflowRunsAsync("ktsu-dev", "a", It.IsAny<CancellationToken>()))
			.ThrowsAsync(new NotFoundException("Not Found", HttpStatusCode.NotFound));
		api.Setup(s => s.GetWorkflowRunsAsync("ktsu-dev", "b", It.IsAny<CancellationToken>()))
			.ReturnsAsync([Run("in_progress", completedAt: null)]);

		BuildMonitorService service = new(api.Object);
		IRenderable rendered = await service.RenderDashboardAsync("ktsu-dev", CancellationToken.None).ConfigureAwait(false);
		string output = await RenderAsync(rendered).ConfigureAwait(false);

		Assert.IsInstanceOfType<Table>(rendered, "One repository's error must not replace the whole dashboard.");
		StringAssert.Contains(output, "Error");
		StringAssert.Contains(output, "In progress...");
	}

	[TestMethod]
	[DataRow("queued")]
	[DataRow("in_progress")]
	[DataRow("waiting")]
	public void CompletedAtOfIsNullForARunThatHasNotCompleted(string status)
	{
		DateTimeOffset updated = DateTimeOffset.Parse("2026-11-02T10:00:00Z", CultureInfo.InvariantCulture);

		Assert.IsNull(GitHubService.CompletedAtOf(new StringEnum<WorkflowRunStatus>(status), updated), "A run that has not completed must show as in progress.");
	}

	[TestMethod]
	public void CompletedAtOfIsTheLastUpdateForACompletedRun()
	{
		DateTimeOffset updated = DateTimeOffset.Parse("2026-11-02T10:00:00Z", CultureInfo.InvariantCulture);

		Assert.AreEqual(updated, GitHubService.CompletedAtOf(new StringEnum<WorkflowRunStatus>("completed"), updated));
	}

	[TestMethod]
	public async Task GetRepositoriesListsAnOrganizationThroughItsOwnEndpoint()
	{
		(Mock<IGitHubClient> client, Mock<IRepositoriesClient> repositories) = ClientFor("Organization");
		repositories.Setup(r => r.GetAllForOrg("ktsu-dev")).ReturnsAsync(Repositories("private-one"));

		IReadOnlyList<GitHubRepository> repos = await new GitHubService(client.Object).GetRepositoriesAsync("ktsu-dev").ConfigureAwait(false);

		Assert.AreEqual("private-one", repos.Single().Name, "An organization's private repositories are only listed by /orgs/{org}/repos.");
		repositories.Verify(r => r.GetAllForUser(It.IsAny<string>()), Times.Never);
	}

	[TestMethod]
	public async Task GetRepositoriesListsAUserThroughTheUserEndpoint()
	{
		(Mock<IGitHubClient> client, Mock<IRepositoriesClient> repositories) = ClientFor("User");
		repositories.Setup(r => r.GetAllForUser("ktsu-dev")).ReturnsAsync(Repositories("mine"));

		IReadOnlyList<GitHubRepository> repos = await new GitHubService(client.Object).GetRepositoriesAsync("ktsu-dev").ConfigureAwait(false);

		Assert.AreEqual("mine", repos.Single().Name);
		repositories.Verify(r => r.GetAllForOrg(It.IsAny<string>()), Times.Never);
	}

	[TestMethod]
	public async Task RenderDashboardShowsANetworkFailureInsteadOfThrowing()
	{
		Mock<IGitHubService> api = AuthenticatedApi();
		api.Setup(s => s.GetRepositoriesAsync("ktsu-dev", It.IsAny<CancellationToken>()))
			.ThrowsAsync(new HttpRequestException("No such host is known."));

		string output = await RenderAsync(api.Object).ConfigureAwait(false);

		StringAssert.Contains(output, "API Error: No such host is known.");
	}

	[TestMethod]
	public async Task GetWorkflowRunsShowsOnlyACompletedRunAsCompleted()
	{
		Mock<IGitHubClient> client = new() { DefaultValue = DefaultValue.Mock };
		WorkflowRunsResponse response = new SimpleJsonSerializer().Deserialize<WorkflowRunsResponse>(
			"""
			{"total_count":2,"workflow_runs":[
			{"id":1,"name":"CI","status":"in_progress","conclusion":null,"head_branch":"main","html_url":"https://github.com/ktsu-dev/a/actions/runs/1","created_at":"2026-11-02T10:00:00Z","updated_at":"2026-11-02T10:05:00Z"},
			{"id":2,"name":"CI","status":"completed","conclusion":"success","head_branch":"main","html_url":"https://github.com/ktsu-dev/a/actions/runs/2","created_at":"2026-11-02T09:00:00Z","updated_at":"2026-11-02T09:07:00Z"}]}
			""");
		client.Setup(c => c.Actions.Workflows.Runs.List("ktsu-dev", "a")).ReturnsAsync(response);

		IReadOnlyList<GitHubWorkflowRun> runs = await new GitHubService(client.Object).GetWorkflowRunsAsync("ktsu-dev", "a").ConfigureAwait(false);

		Assert.IsNull(runs[0].CompletedAt, "A running run must not get a completion time.");
		Assert.AreEqual(DateTimeOffset.Parse("2026-11-02T09:07:00Z", CultureInfo.InvariantCulture), runs[1].CompletedAt);
		Assert.AreEqual("success", runs[1].Conclusion);
	}

	[TestMethod]
	public async Task GetWorkflowRunsReadsTheLatestAttemptsStart()
	{
		Mock<IGitHubClient> client = new() { DefaultValue = DefaultValue.Mock };
		WorkflowRunsResponse response = new SimpleJsonSerializer().Deserialize<WorkflowRunsResponse>(
			"""
			{"total_count":2,"workflow_runs":[
			{"id":1,"name":"CI","status":"completed","conclusion":"success","head_branch":"main","html_url":"https://github.com/ktsu-dev/a/actions/runs/1","created_at":"2026-11-02T09:00:00Z","run_started_at":"2026-11-04T14:00:00Z","run_attempt":2,"updated_at":"2026-11-04T14:03:00Z"},
			{"id":2,"name":"CI","status":"completed","conclusion":"success","head_branch":"main","html_url":"https://github.com/ktsu-dev/a/actions/runs/2","created_at":"2026-11-02T08:00:00Z","updated_at":"2026-11-02T08:04:00Z"}]}
			""");
		client.Setup(c => c.Actions.Workflows.Runs.List("ktsu-dev", "a")).ReturnsAsync(response);

		IReadOnlyList<GitHubWorkflowRun> runs = await new GitHubService(client.Object).GetWorkflowRunsAsync("ktsu-dev", "a").ConfigureAwait(false);

		Assert.AreEqual(DateTimeOffset.Parse("2026-11-04T14:00:00Z", CultureInfo.InvariantCulture), runs[0].StartedAt, "A re-run starts at run_started_at, not at the first attempt's created_at.");
		Assert.AreEqual(2, runs[0].Attempt);
		Assert.AreEqual(DateTimeOffset.Parse("2026-11-02T08:00:00Z", CultureInfo.InvariantCulture), runs[1].StartedAt, "Without run_started_at the run started when it was created.");
		Assert.AreEqual(1, runs[1].Attempt);
	}

	[TestMethod]
	public async Task RenderDashboardTimesARerunFromItsLatestAttempt()
	{
		DateTimeOffset now = DateTimeOffset.UtcNow;
		GitHubWorkflowRun rerun = new(1, "CI", "completed", "success", "main", new Uri("https://github.com/ktsu-dev/a/actions/runs/1"),
			CreatedAt: now.AddDays(-2), StartedAt: now.AddMinutes(-6), Attempt: 2, CompletedAt: now.AddMinutes(-3));
		Mock<IGitHubService> api = AuthenticatedApi();
		api.Setup(s => s.GetRepositoriesAsync("ktsu-dev", It.IsAny<CancellationToken>())).ReturnsAsync([RepoA]);
		api.Setup(s => s.GetWorkflowRunsAsync("ktsu-dev", "a", It.IsAny<CancellationToken>())).ReturnsAsync([rerun]);

		string output = await RenderAsync(api.Object).ConfigureAwait(false);

		StringAssert.Contains(output, "3 minutes", "The duration is the latest attempt's, not the time since the first.");
		StringAssert.Contains(output, "6 minutes ago");
		StringAssert.Contains(output, "attempt 2");
		Assert.IsFalse(output.Contains("days", StringComparison.Ordinal), "Nothing about this attempt took or started days ago.");
	}

	[TestMethod]
	public async Task ProjectRunsTableShowsWhenTheLatestAttemptStarted()
	{
		DateTimeOffset created = DateTimeOffset.Parse("2026-11-02T09:00:00Z", CultureInfo.InvariantCulture);
		DateTimeOffset started = DateTimeOffset.Parse("2026-11-04T14:00:00Z", CultureInfo.InvariantCulture);
		GitHubWorkflowRun rerun = new(1, "CI", "completed", "success", "main", new Uri("https://github.com/ktsu-dev/a/actions/runs/1"),
			CreatedAt: created, StartedAt: started, Attempt: 2, CompletedAt: started.AddMinutes(3));

		string output = await RenderAsync(KtsuTools.Project.ProjectService.BuildRunsTable([rerun])).ConfigureAwait(false);

		StringAssert.Contains(output, "Started");
		StringAssert.Contains(output, started.ToString("g", CultureInfo.InvariantCulture));
		Assert.IsFalse(output.Contains(created.ToString("g", CultureInfo.InvariantCulture), StringComparison.Ordinal), "A re-run is listed by its latest attempt, not by the first.");
	}

	[TestMethod]
	public async Task GetRateLimitReadsTheCoreLimit()
	{
		Mock<IGitHubClient> client = new() { DefaultValue = DefaultValue.Mock };
		MiscellaneousRateLimit limits = new SimpleJsonSerializer().Deserialize<MiscellaneousRateLimit>(
			"""{"resources":{"core":{"limit":5000,"remaining":4321,"reset":1762077600}},"rate":{"limit":5000,"remaining":4321,"reset":1762077600}}""");
		client.Setup(c => c.RateLimit.GetRateLimits()).ReturnsAsync(limits);

		GitHubRateLimitInfo info = await new GitHubService(client.Object).GetRateLimitAsync().ConfigureAwait(false);

		Assert.AreEqual(4321, info.Remaining);
		Assert.AreEqual(5000, info.Limit);
	}

	[TestMethod]
	public async Task RerunAndCancelReportWhetherGitHubAcceptedThem()
	{
		Mock<IGitHubClient> client = new() { DefaultValue = DefaultValue.Mock };
		client.Setup(c => c.Actions.Workflows.Runs.Rerun("ktsu-dev", "a", 1)).ThrowsAsync(new NotFoundException("Not Found", HttpStatusCode.NotFound));
		client.Setup(c => c.Actions.Workflows.Runs.Cancel("ktsu-dev", "a", 1)).Returns(Task.CompletedTask);
		GitHubService service = new(client.Object);

		Assert.IsFalse(await service.RerunWorkflowAsync("ktsu-dev", "a", 1).ConfigureAwait(false));
		Assert.IsTrue(await service.CancelWorkflowAsync("ktsu-dev", "a", 1).ConfigureAwait(false));
	}

	private static Mock<IGitHubService> AuthenticatedApi()
	{
		Mock<IGitHubService> api = new();
		api.SetupGet(s => s.IsAuthenticated).Returns(true);
		api.Setup(s => s.GetRateLimitAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new GitHubRateLimitInfo(4000, 5000, DateTimeOffset.UtcNow.AddMinutes(30)));
		return api;
	}

	private static GitHubWorkflowRun Run(string status, DateTimeOffset? completedAt)
	{
		DateTimeOffset created = DateTimeOffset.UtcNow.AddMinutes(-5);
		return new(1, "CI", status, "pending", "main", new Uri("https://github.com/ktsu-dev/b/actions/runs/1"), created, created, 1, completedAt);
	}

	private static IResponse Response(HttpStatusCode status, bool rateLimited = false)
	{
		Dictionary<string, string> headers = rateLimited
			? new()
			{
				["X-RateLimit-Limit"] = "5000",
				["X-RateLimit-Remaining"] = "0",
				["X-RateLimit-Reset"] = DateTimeOffset.UtcNow.AddMinutes(20).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
			}
			: [];

		Mock<IResponse> response = new();
		response.SetupGet(r => r.StatusCode).Returns(status);
		response.SetupGet(r => r.Headers).Returns(headers);
		response.SetupGet(r => r.Body).Returns("{\"message\":\"Bad credentials\"}");
		response.SetupGet(r => r.ContentType).Returns("application/json");
		if (rateLimited)
		{
			RateLimit limit = new(5000, 0, DateTimeOffset.UtcNow.AddMinutes(20).ToUnixTimeSeconds());
			response.SetupGet(r => r.ApiInfo).Returns(new ApiInfo(new Dictionary<string, Uri>(), [], [], string.Empty, limit));
		}

		return response.Object;
	}

	private static (Mock<IGitHubClient> Client, Mock<IRepositoriesClient> Repositories) ClientFor(string accountType)
	{
		SimpleJsonSerializer json = new();
		User account = json.Deserialize<User>($"{{\"login\":\"ktsu-dev\",\"type\":\"{accountType}\"}}");

		Mock<IUsersClient> users = new();
		users.Setup(u => u.Get("ktsu-dev")).ReturnsAsync(account);

		Mock<IRepositoriesClient> repositories = new();
		Mock<IGitHubClient> client = new();
		client.SetupGet(c => c.User).Returns(users.Object);
		client.SetupGet(c => c.Repository).Returns(repositories.Object);
		return (client, repositories);
	}

	private static List<Repository> Repositories(string name) =>
		new SimpleJsonSerializer().Deserialize<List<Repository>>(
			$"[{{\"id\":1,\"name\":\"{name}\",\"full_name\":\"ktsu-dev/{name}\",\"html_url\":\"https://github.com/ktsu-dev/{name}\",\"default_branch\":\"main\"}}]");

	private static async Task<string> RenderAsync(IGitHubService api)
	{
		IRenderable rendered = await new BuildMonitorService(api).RenderDashboardAsync("ktsu-dev", CancellationToken.None).ConfigureAwait(false);
		return await RenderAsync(rendered).ConfigureAwait(false);
	}

	private static Task<string> RenderAsync(IRenderable rendered) =>
		ConsoleCapture.CaptureAsync(() =>
		{
			AnsiConsole.Write(rendered);
			return Task.CompletedTask;
		});
}
