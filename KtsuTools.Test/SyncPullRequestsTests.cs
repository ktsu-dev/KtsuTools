// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Core.Services.GitHub;
using KtsuTools.Core.Services.Process;
using KtsuTools.Sync;

[TestClass]
public class SyncPullRequestsTests
{
	private const string Branch = "sync/shared";
	private const string BaseBranch = "main";

	[TestMethod]
	public void ParseGitHubSlugReadsAnHttpsRemote()
	{
		GitHubSlug? slug = SyncPullRequestOpener.ParseGitHubSlug("https://github.com/ktsu-dev/KtsuTools.git");

		Assert.IsNotNull(slug);
		Assert.AreEqual("ktsu-dev", slug.Owner);
		Assert.AreEqual("KtsuTools", slug.Name);
	}

	[TestMethod]
	public void ParseGitHubSlugReadsAnHttpsRemoteWithoutTheGitSuffix()
	{
		GitHubSlug? slug = SyncPullRequestOpener.ParseGitHubSlug("https://github.com/ktsu-dev/KtsuTools");

		Assert.IsNotNull(slug);
		Assert.AreEqual("ktsu-dev/KtsuTools", slug.ToString());
	}

	[TestMethod]
	public void ParseGitHubSlugReadsAnScpStyleSshRemote()
	{
		GitHubSlug? slug = SyncPullRequestOpener.ParseGitHubSlug("git@github.com:ktsu-dev/KtsuTools.git");

		Assert.IsNotNull(slug);
		Assert.AreEqual("ktsu-dev/KtsuTools", slug.ToString());
	}

	[TestMethod]
	public void ParseGitHubSlugReadsAnSshSchemeRemote()
	{
		GitHubSlug? slug = SyncPullRequestOpener.ParseGitHubSlug("ssh://git@github.com/ktsu-dev/KtsuTools.git");

		Assert.IsNotNull(slug);
		Assert.AreEqual("ktsu-dev/KtsuTools", slug.ToString());
	}

	[TestMethod]
	public void ParseGitHubSlugRejectsANonGitHubRemote()
	{
		Assert.IsNull(SyncPullRequestOpener.ParseGitHubSlug("https://gitlab.com/ktsu-dev/KtsuTools.git"));
		Assert.IsNull(SyncPullRequestOpener.ParseGitHubSlug("git@bitbucket.org:ktsu-dev/KtsuTools.git"));
	}

	[TestMethod]
	public void ParseGitHubSlugRejectsAnAddressThatNamesNoRepository()
	{
		Assert.IsNull(SyncPullRequestOpener.ParseGitHubSlug(null));
		Assert.IsNull(SyncPullRequestOpener.ParseGitHubSlug("   "));
		Assert.IsNull(SyncPullRequestOpener.ParseGitHubSlug("https://github.com/ktsu-dev"));
		Assert.IsNull(SyncPullRequestOpener.ParseGitHubSlug("https://github.com/ktsu-dev/KtsuTools/tree/main"));
	}

	[TestMethod]
	public void QuoteArgumentWrapsAValueSoTheSplitterReturnsItWhole()
	{
		Assert.AreEqual("\"plain\"", SyncPullRequestOpener.QuoteArgument("plain"));
		Assert.AreEqual("\"two words\"", SyncPullRequestOpener.QuoteArgument("two words"));
	}

	[TestMethod]
	public void QuoteArgumentEscapesAnEmbeddedQuote()
	{
		// A bare quote would end the argument, so it is escaped rather than passed through.
		Assert.AreEqual("\"say \\\"hi\\\"\"", SyncPullRequestOpener.QuoteArgument("say \"hi\""));
	}

	[TestMethod]
	public void QuoteArgumentDoublesTrailingBackslashesSoTheyDoNotEscapeTheClosingQuote()
	{
		Assert.AreEqual("\"path\\\\\"", SyncPullRequestOpener.QuoteArgument("path\\"));
		Assert.AreEqual("\"a\\b\"", SyncPullRequestOpener.QuoteArgument("a\\b"));
	}

	[TestMethod]
	public void BuildListArgumentsAsksGhForOpenPullRequestsOnTheBranch()
	{
		string arguments = SyncPullRequestOpener.BuildListArguments(Branch);

		StringAssert.Contains(arguments, "pr list");
		StringAssert.Contains(arguments, "--head \"sync/shared\"");
		StringAssert.Contains(arguments, "--state open");
		StringAssert.Contains(arguments, "--json url");
	}

	[TestMethod]
	public void BuildCreateArgumentsCarriesTheBranchesTitleAndBody()
	{
		string arguments = SyncPullRequestOpener.BuildCreateArguments(Branch, BaseBranch, "Sync .editorconfig", "a body");

		StringAssert.Contains(arguments, "pr create");
		StringAssert.Contains(arguments, "--head \"sync/shared\"");
		StringAssert.Contains(arguments, "--base \"main\"");
		StringAssert.Contains(arguments, "--title \"Sync .editorconfig\"");
		StringAssert.Contains(arguments, "--body \"a body\"");
	}

	[TestMethod]
	public void ParseFirstPullRequestUrlReadsTheFirstEntry()
	{
		Uri? url = SyncPullRequestOpener.ParseFirstPullRequestUrl(
			"""[{"url":"https://github.com/ktsu-dev/KtsuTools/pull/7"}]""");

		Assert.IsNotNull(url);
		Assert.AreEqual("https://github.com/ktsu-dev/KtsuTools/pull/7", url.ToString());
	}

	[TestMethod]
	public void ParseFirstPullRequestUrlTreatsAnEmptyOrUnreadableListAsNone()
	{
		Assert.IsNull(SyncPullRequestOpener.ParseFirstPullRequestUrl("[]"));
		Assert.IsNull(SyncPullRequestOpener.ParseFirstPullRequestUrl(string.Empty));
		Assert.IsNull(SyncPullRequestOpener.ParseFirstPullRequestUrl("not json"));
		Assert.IsNull(SyncPullRequestOpener.ParseFirstPullRequestUrl("""{"url":"https://example.test"}"""));
	}

	[TestMethod]
	public void BuildTitleNamesTheSyncedFiles()
	{
		Assert.AreEqual("Sync shared files", SyncPullRequestOpener.BuildTitle([]));
		Assert.AreEqual("Sync .editorconfig", SyncPullRequestOpener.BuildTitle([Synced(".editorconfig")]));
		Assert.AreEqual(
			"Sync .editorconfig and .gitignore",
			SyncPullRequestOpener.BuildTitle([Synced(".editorconfig"), Synced(".gitignore")]));
		Assert.AreEqual(
			"Sync .editorconfig, .gitignore and 1 more",
			SyncPullRequestOpener.BuildTitle([Synced(".editorconfig"), Synced(".gitignore"), Synced("global.json")]));
	}

	[TestMethod]
	public void BuildBodyRecordsEachFileAndTheVersionItCameFrom()
	{
		string body = SyncPullRequestOpener.BuildBody(
			[new SyncedFile(".editorconfig", "ABCDEF0123456789", "repo-a")],
			Branch);

		StringAssert.Contains(body, "sync/shared");
		StringAssert.Contains(body, ".editorconfig");
		StringAssert.Contains(body, "ABCDEF012345", StringComparison.Ordinal);
		StringAssert.Contains(body, "repo-a");
	}

	[TestMethod]
	public void BuildBodySaysSoWhenNothingWasRecorded()
	{
		string body = SyncPullRequestOpener.BuildBody([], Branch);

		StringAssert.Contains(body, "No file differences");
	}

	[TestMethod]
	public void ShortHashKeepsAShortHashWhole()
	{
		Assert.AreEqual("ABCDEF012345", SyncPullRequestOpener.ShortHash("ABCDEF0123456789"));
		Assert.AreEqual("ABCD", SyncPullRequestOpener.ShortHash("ABCD"));
		Assert.AreEqual(string.Empty, SyncPullRequestOpener.ShortHash(string.Empty));
	}

	[TestMethod]
	public async Task OpenAsyncCreatesAPullRequestWhenTheBranchHasNone()
	{
		using TempRemoteRepo repo = TempRemoteRepo.On("https://github.com/ktsu-dev/KtsuTools.git");
		ScriptedProcessService gh = ScriptedProcessService.WithNoOpenPullRequest();

		await OpenerFor(gh).OpenAsync([repo.Root], Branch, BaseBranchOf(repo), [Synced(".editorconfig")], CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(1, gh.CreateCalls.Count, "A branch with no open pull request should get one.");
		StringAssert.Contains(gh.CreateCalls[0], "--base \"main\"");
		StringAssert.Contains(gh.CreateCalls[0], "--head \"sync/shared\"");
	}

	[TestMethod]
	public async Task OpenAsyncNoOpsWhenAPullRequestIsAlreadyOpenForTheBranch()
	{
		using TempRemoteRepo repo = TempRemoteRepo.On("https://github.com/ktsu-dev/KtsuTools.git");
		ScriptedProcessService gh = ScriptedProcessService.WithOpenPullRequest("https://github.com/ktsu-dev/KtsuTools/pull/7");

		await OpenerFor(gh).OpenAsync([repo.Root], Branch, BaseBranchOf(repo), [Synced(".editorconfig")], CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(0, gh.CreateCalls.Count, "A second pull request must not be opened for a branch that already has one.");
	}

	[TestMethod]
	public async Task OpenAsyncSkipsARepositoryWhoseRemoteIsNotOnGitHub()
	{
		using TempRemoteRepo repo = TempRemoteRepo.On("https://gitlab.com/ktsu-dev/KtsuTools.git");
		ScriptedProcessService gh = ScriptedProcessService.WithNoOpenPullRequest();

		await OpenerFor(gh).OpenAsync([repo.Root], Branch, BaseBranchOf(repo), [Synced(".editorconfig")], CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(0, gh.CreateCalls.Count, "A non-GitHub remote is skipped rather than failing the run.");
	}

	[TestMethod]
	public async Task OpenAsyncSkipsARepositoryWithNoRecordedBaseBranch()
	{
		using TempRemoteRepo repo = TempRemoteRepo.On("https://github.com/ktsu-dev/KtsuTools.git");
		ScriptedProcessService gh = ScriptedProcessService.WithNoOpenPullRequest();

		await OpenerFor(gh).OpenAsync([repo.Root], Branch, new Dictionary<string, string>(), [Synced(".editorconfig")], CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(0, gh.CreateCalls.Count, "A pull request cannot be opened without knowing what it targets.");
	}

	[TestMethod]
	public async Task OpenAsyncFallsBackToTheApiWhenTheGhCliIsAbsent()
	{
		using TempRemoteRepo repo = TempRemoteRepo.On("https://github.com/ktsu-dev/KtsuTools.git");
		ScriptedProcessService gh = ScriptedProcessService.WithoutTheGhCli();
		RecordingGitHubService api = new() { IsAuthenticated = true };

		await new SyncPullRequestOpener(gh, api)
			.OpenAsync([repo.Root], Branch, BaseBranchOf(repo), [Synced(".editorconfig")], CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(0, gh.CreateCalls.Count, "gh is not installed, so it must not be driven.");
		Assert.AreEqual(1, api.Created.Count, "The API is the fallback when gh is absent.");
		Assert.AreEqual(("ktsu-dev", "KtsuTools", Branch, BaseBranch), api.Created[0]);
	}

	[TestMethod]
	public async Task OpenAsyncDoesNotReachTheApiWhenAPullRequestIsAlreadyOpen()
	{
		using TempRemoteRepo repo = TempRemoteRepo.On("https://github.com/ktsu-dev/KtsuTools.git");
		ScriptedProcessService gh = ScriptedProcessService.WithoutTheGhCli();
		RecordingGitHubService api = new()
		{
			IsAuthenticated = true,
			ExistingPullRequest = new Uri("https://github.com/ktsu-dev/KtsuTools/pull/7"),
		};

		await new SyncPullRequestOpener(gh, api)
			.OpenAsync([repo.Root], Branch, BaseBranchOf(repo), [Synced(".editorconfig")], CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(0, api.Created.Count, "A second pull request must not be opened for a branch that already has one.");
	}

	[TestMethod]
	public async Task OpenAsyncDoesNothingWhenNoRepositoryWasPushed()
	{
		ScriptedProcessService gh = ScriptedProcessService.WithNoOpenPullRequest();

		await OpenerFor(gh).OpenAsync([], Branch, new Dictionary<string, string>(), [Synced(".editorconfig")], CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(0, gh.ProbeCalls, "Nothing was pushed, so gh should not even be probed.");
	}

	[TestMethod]
	public async Task OpenAsyncSkipsEverythingWhenThereIsNeitherAGhCliNorAToken()
	{
		using TempRemoteRepo repo = TempRemoteRepo.On("https://github.com/ktsu-dev/KtsuTools.git");
		ScriptedProcessService gh = ScriptedProcessService.WithoutTheGhCli();
		RecordingGitHubService api = new() { IsAuthenticated = false };

		using (NoGitHubToken.InEnvironment())
		{
			await new SyncPullRequestOpener(gh, api)
				.OpenAsync([repo.Root], Branch, BaseBranchOf(repo), [Synced(".editorconfig")], CancellationToken.None)
				.ConfigureAwait(false);
		}

		Assert.AreEqual(0, gh.CreateCalls.Count);
		Assert.AreEqual(0, api.Created.Count, "With no gh and no token there is no way to open anything.");
	}

	[TestMethod]
	public async Task OpenAsyncAuthenticatesTheApiFromAnEnvironmentToken()
	{
		using TempRemoteRepo repo = TempRemoteRepo.On("https://github.com/ktsu-dev/KtsuTools.git");
		ScriptedProcessService gh = ScriptedProcessService.WithoutTheGhCli();
		RecordingGitHubService api = new() { IsAuthenticated = false };

		using (NoGitHubToken.InEnvironment("GH_TOKEN", "a-token"))
		{
			await new SyncPullRequestOpener(gh, api)
				.OpenAsync([repo.Root], Branch, BaseBranchOf(repo), [Synced(".editorconfig")], CancellationToken.None)
				.ConfigureAwait(false);
		}

		Assert.AreEqual("a-token", api.InitializedWith, "The token in the environment is what authenticates the fallback.");
		Assert.AreEqual(1, api.Created.Count);
	}

	[TestMethod]
	public async Task OpenAsyncTreatsAGhCliThatIsNotOnThePathAsAbsent()
	{
		using TempRemoteRepo repo = TempRemoteRepo.On("https://github.com/ktsu-dev/KtsuTools.git");
		ThrowingProcessService gh = new();
		RecordingGitHubService api = new() { IsAuthenticated = true };

		await new SyncPullRequestOpener(gh, api)
			.OpenAsync([repo.Root], Branch, BaseBranchOf(repo), [Synced(".editorconfig")], CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(1, api.Created.Count, "A gh that cannot be launched at all must fall through to the API.");
	}

	[TestMethod]
	public async Task OpenAsyncReportsAGhCreateThatFailsAndKeepsGoing()
	{
		using TempRemoteRepo first = TempRemoteRepo.On("https://github.com/ktsu-dev/KtsuTools.git");
		using TempRemoteRepo second = TempRemoteRepo.On("https://github.com/ktsu-dev/Sorting.git");
		ScriptedProcessService gh = ScriptedProcessService.WithAFailingCreate();

		Dictionary<string, string> bases = new(StringComparer.Ordinal)
		{
			[first.Root] = BaseBranch,
			[second.Root] = BaseBranch,
		};

		await OpenerFor(gh).OpenAsync([first.Root, second.Root], Branch, bases, [Synced(".editorconfig")], CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(2, gh.CreateCalls.Count, "One repository's rejected pull request must not abandon the rest of the run.");
	}

	[TestMethod]
	public async Task OpenAsyncReportsAnApiThatRejectsThePullRequest()
	{
		using TempRemoteRepo repo = TempRemoteRepo.On("https://github.com/ktsu-dev/KtsuTools.git");
		ScriptedProcessService gh = ScriptedProcessService.WithoutTheGhCli();
		RecordingGitHubService api = new() { IsAuthenticated = true, RejectCreate = true };

		await new SyncPullRequestOpener(gh, api)
			.OpenAsync([repo.Root], Branch, BaseBranchOf(repo), [Synced(".editorconfig")], CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(1, api.Created.Count, "The attempt is still made; only its result is a rejection.");
	}

	[TestMethod]
	public async Task RemoteUrlOfIsNullForADirectoryThatIsNotARepository()
	{
		string notARepo = Path.Join(Path.GetTempPath(), $"ktsu_sync_pr_bare_{Guid.NewGuid():N}");
		Directory.CreateDirectory(notARepo);
		try
		{
			Assert.IsNull(await SyncPullRequestOpener.RemoteUrlOfAsync(notARepo).ConfigureAwait(false));
		}
		finally
		{
			Directory.Delete(notARepo, recursive: true);
		}
	}

	[TestMethod]
	public async Task RemoteUrlOfIsNullForARepositoryWithNoOrigin()
	{
		using TempRemoteRepo repo = TempRemoteRepo.WithoutARemote();

		Assert.IsNull(await SyncPullRequestOpener.RemoteUrlOfAsync(repo.Root).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task OpenPullRequestsAsyncDoesNothingWhenNothingWasPushed()
	{
		ScriptedProcessService process = ScriptedProcessService.WithNoOpenPullRequest();

		await new SyncService(process, new RecordingGitHubService())
			.OpenPullRequestsAsync([], Branch, [], [Synced(".editorconfig")], CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(0, process.ProbeCalls);
		Assert.AreEqual(0, process.CreateCalls.Count);
	}

	[TestMethod]
	public async Task OpenPullRequestsAsyncSkipsWhenNoGitHubServiceIsAvailable()
	{
		using TempRemoteRepo repo = TempRemoteRepo.On("https://github.com/ktsu-dev/KtsuTools.git");
		ScriptedProcessService process = ScriptedProcessService.WithNoOpenPullRequest();

		await new SyncService(process)
			.OpenPullRequestsAsync(
				[repo.Root],
				Branch,
				[new SyncService.BranchSwitch(repo.Root, BaseBranch, "aaaa1111")],
				[Synced(".editorconfig")],
				CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(0, process.CreateCalls.Count, "Without a GitHub service there is nothing to open a pull request with.");
	}

	[TestMethod]
	public async Task OpenPullRequestsAsyncOpensOneForEachPushedRepository()
	{
		using TempRemoteRepo repo = TempRemoteRepo.On("https://github.com/ktsu-dev/KtsuTools.git");
		ScriptedProcessService gh = ScriptedProcessService.WithNoOpenPullRequest();

		await new SyncService(gh, new RecordingGitHubService())
			.OpenPullRequestsAsync(
				[repo.Root],
				Branch,
				[new SyncService.BranchSwitch(repo.Root, BaseBranch, "aaaa1111")],
				[Synced(".editorconfig")],
				CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(1, gh.CreateCalls.Count);
		StringAssert.Contains(gh.CreateCalls[0], "--base \"main\"");
	}

	[TestMethod]
	public async Task PushDirectoryAsyncReportsAFailedPush()
	{
		using TempRemoteRepo repo = TempRemoteRepo.On("https://github.com/ktsu-dev/KtsuTools.git");
		FailingPushService push = new();

		bool pushed = await new SyncService(push)
			.PushDirectoryAsync(repo.Root, Branch, CancellationToken.None)
			.ConfigureAwait(false);

		Assert.IsFalse(pushed, "A repository whose push failed must not be reported as pushed.");
	}

	[TestMethod]
	public async Task PushDirectoryAsyncReportsASuccessfulPush()
	{
		using TempRemoteRepo repo = TempRemoteRepo.On("https://github.com/ktsu-dev/KtsuTools.git");
		ScriptedProcessService push = ScriptedProcessService.WithNoOpenPullRequest();

		bool pushed = await new SyncService(push)
			.PushDirectoryAsync(repo.Root, Branch, CancellationToken.None)
			.ConfigureAwait(false);

		Assert.IsTrue(pushed);
	}

	[TestMethod]
	public void CreatePullRequestBeforeInitializingIsAProgrammingError()
	{
		GitHubService service = new();

		Assert.IsFalse(service.IsAuthenticated);
		_ = Assert.ThrowsExactlyAsync<InvalidOperationException>(
			async () => await service.CreatePullRequestAsync("o", "r", "head", "base", "t", "b", CancellationToken.None).ConfigureAwait(false));
	}

	[TestMethod]
	public void FindOpenPullRequestBeforeInitializingIsAProgrammingError()
	{
		GitHubService service = new();

		_ = Assert.ThrowsExactlyAsync<InvalidOperationException>(
			async () => await service.FindOpenPullRequestAsync("o", "r", "head", CancellationToken.None).ConfigureAwait(false));
	}

	[TestMethod]
	public void BaseBranchesForKeepsTheBranchEachRepoWasOn()
	{
		Dictionary<string, string> bases = SyncService.BaseBranchesFor(
		[
			new SyncService.BranchSwitch("/repo-a", "main", "aaaa1111"),
			new SyncService.BranchSwitch("/repo-b", "develop", "bbbb2222"),
		]);

		Assert.AreEqual(2, bases.Count);
		Assert.AreEqual("main", bases["/repo-a"]);
		Assert.AreEqual("develop", bases["/repo-b"]);
	}

	[TestMethod]
	public void BaseBranchesForLeavesOutADetachedHead()
	{
		// A detached HEAD is recorded as its own sha, and a pull request cannot target a commit.
		Dictionary<string, string> bases = SyncService.BaseBranchesFor(
		[
			new SyncService.BranchSwitch("/repo-a", "cccc3333", "cccc3333"),
		]);

		Assert.AreEqual(0, bases.Count);
	}

	private static SyncedFile Synced(string fileName) => new(fileName, "ABCDEF0123456789", "repo-a");

	private static SyncPullRequestOpener OpenerFor(IProcessService processService) =>
		new(processService, new RecordingGitHubService());

	private static Dictionary<string, string> BaseBranchOf(TempRemoteRepo repo) =>
		new(StringComparer.Ordinal) { [repo.Root] = BaseBranch };

	/// <summary>
	/// A process service that answers the three calls the opener makes — the gh probe, the pull
	/// request list, and the create — so the gh path is exercised without gh being installed.
	/// </summary>
	private sealed class ScriptedProcessService(int probeExitCode, string listOutput, int createExitCode = 0) : IProcessService
	{
		public List<string> CreateCalls { get; } = [];

		public int ProbeCalls { get; private set; }

		public static ScriptedProcessService WithNoOpenPullRequest() => new(0, "[]");

		public static ScriptedProcessService WithOpenPullRequest(string url) =>
			new(0, $$"""[{"url":"{{url}}"}]""");

		public static ScriptedProcessService WithoutTheGhCli() => new(1, "[]");

		public static ScriptedProcessService WithAFailingCreate() => new(0, "[]", createExitCode: 1);

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			RunAsync(command, arguments, workingDirectory, null, ct);

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default)
		{
			if (arguments == "--version")
			{
				ProbeCalls++;
				return Task.FromResult(new ProcessResult(probeExitCode, ["gh version 2.0.0"], []));
			}

			if (arguments.StartsWith("pr list", StringComparison.Ordinal))
			{
				return Task.FromResult(new ProcessResult(0, [listOutput], []));
			}

			CreateCalls.Add(arguments);
			return createExitCode == 0
				? Task.FromResult(new ProcessResult(0, ["https://github.com/ktsu-dev/KtsuTools/pull/9"], []))
				: Task.FromResult(new ProcessResult(createExitCode, [], ["pull request create failed"]));
		}
	}

	/// <summary>
	/// A process service whose every call fails, so the push-failure path is exercised.
	/// </summary>
	private sealed class FailingPushService : IProcessService
	{
		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			RunAsync(command, arguments, workingDirectory, null, ct);

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default) =>
			Task.FromResult(new ProcessResult(1, [], ["remote rejected"]));
	}

	/// <summary>
	/// A process service that fails to launch at all, the way a missing executable does.
	/// </summary>
	private sealed class ThrowingProcessService : IProcessService
	{
		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			RunAsync(command, arguments, workingDirectory, null, ct);

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default) =>
			throw new System.ComponentModel.Win32Exception(2, "No such file or directory");
	}

	/// <summary>
	/// Clears both token variables for the duration of a test and restores whatever was there,
	/// so a token in the developer's or runner's environment cannot change the outcome.
	/// </summary>
	private sealed class NoGitHubToken : IDisposable
	{
		private readonly string? originalGhToken = Environment.GetEnvironmentVariable("GH_TOKEN");
		private readonly string? originalGitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

		private NoGitHubToken()
		{
		}

		public static NoGitHubToken InEnvironment()
		{
			NoGitHubToken scope = new();
			Environment.SetEnvironmentVariable("GH_TOKEN", null);
			Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
			return scope;
		}

		public static NoGitHubToken InEnvironment(string name, string value)
		{
			NoGitHubToken scope = InEnvironment();
			Environment.SetEnvironmentVariable(name, value);
			return scope;
		}

		public void Dispose()
		{
			Environment.SetEnvironmentVariable("GH_TOKEN", originalGhToken);
			Environment.SetEnvironmentVariable("GITHUB_TOKEN", originalGitHubToken);
		}
	}

	/// <summary>
	/// A GitHub service that records what it was asked to do rather than reaching the network.
	/// </summary>
	private sealed class RecordingGitHubService : IGitHubService
	{
		public List<(string Owner, string Repo, string Head, string Base)> Created { get; } = [];

		public Uri? ExistingPullRequest { get; init; }

		public bool RejectCreate { get; init; }

		public string? InitializedWith { get; private set; }

		public bool IsAuthenticated { get; set; }

		public Task InitializeAsync(string token, CancellationToken ct = default)
		{
			InitializedWith = token;
			IsAuthenticated = true;
			return Task.CompletedTask;
		}

		public Task<Uri?> FindOpenPullRequestAsync(string owner, string repo, string headBranch, CancellationToken ct = default) =>
			Task.FromResult(ExistingPullRequest);

		public Task<Uri?> CreatePullRequestAsync(string owner, string repo, string headBranch, string baseBranch, string title, string body, CancellationToken ct = default)
		{
			Created.Add((owner, repo, headBranch, baseBranch));
			return Task.FromResult(RejectCreate ? null : new Uri($"https://github.com/{owner}/{repo}/pull/1"));
		}

		public Task<IReadOnlyList<GitHubRepository>> GetRepositoriesAsync(string owner, CancellationToken ct = default) =>
			Task.FromResult<IReadOnlyList<GitHubRepository>>([]);

		public Task<IReadOnlyList<GitHubWorkflowRun>> GetWorkflowRunsAsync(string owner, string repo, CancellationToken ct = default) =>
			Task.FromResult<IReadOnlyList<GitHubWorkflowRun>>([]);

		public Task<GitHubRateLimitInfo> GetRateLimitAsync(CancellationToken ct = default) =>
			Task.FromResult(new GitHubRateLimitInfo(0, 0, DateTimeOffset.UtcNow));

		public Task<bool> RerunWorkflowAsync(string owner, string repo, long runId, CancellationToken ct = default) =>
			Task.FromResult(false);

		public Task<bool> CancelWorkflowAsync(string owner, string repo, long runId, CancellationToken ct = default) =>
			Task.FromResult(false);
	}

	/// <summary>
	/// A throwaway repository carrying an <c>origin</c> remote, so the remote is read through
	/// libgit2 rather than stubbed.
	/// </summary>
	private sealed class TempRemoteRepo : IDisposable
	{
		private TempRemoteRepo(string root) => Root = root;

		public string Root { get; }

		public static TempRemoteRepo On(string remoteUrl)
		{
			TempRemoteRepo created = WithoutARemote();

			TestGit.AddRemote(created.Root, "origin", remoteUrl);

			return created;
		}

		public static TempRemoteRepo WithoutARemote()
		{
			string root = Path.Join(Path.GetTempPath(), $"ktsu_sync_pr_{Guid.NewGuid():N}");
			Directory.CreateDirectory(root);
			TestGit.Init(root);
			return new TempRemoteRepo(root);
		}

		public void Dispose()
		{
			try
			{
				// Git marks objects and pack files read-only, which blocks a plain recursive delete.
				foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
				{
					File.SetAttributes(file, FileAttributes.Normal);
				}

				Directory.Delete(Root, recursive: true);
			}
			catch (DirectoryNotFoundException)
			{
				// Already gone.
			}
		}
	}
}
