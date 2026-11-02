// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Collections.Generic;
using System.Globalization;
using System.Net;
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

	private static Mock<IGitHubService> AuthenticatedApi()
	{
		Mock<IGitHubService> api = new();
		api.SetupGet(s => s.IsAuthenticated).Returns(true);
		api.Setup(s => s.GetRateLimitAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new GitHubRateLimitInfo(4000, 5000, DateTimeOffset.UtcNow.AddMinutes(30)));
		return api;
	}

	private static GitHubWorkflowRun Run(string status, DateTimeOffset? completedAt) =>
		new(1, "CI", status, "pending", "main", new Uri("https://github.com/ktsu-dev/b/actions/runs/1"), DateTimeOffset.UtcNow.AddMinutes(-5), completedAt);

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
