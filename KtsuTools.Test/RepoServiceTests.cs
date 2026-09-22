// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Semantics.Paths;
using KtsuTools.Core.Services.Settings;
using KtsuTools.Core.Services.Git;
using KtsuTools.Core.Services.Process;
using KtsuTools.Repo;
using Moq;
using Spectre.Console;

[TestClass]
public class RepoServiceTests
{
	private static readonly string[] RepoAThenRepoB = ["repo-a", "repo-b"];
	private static readonly string[] AlphaThenBeta = ["alpha", "beta"];

	[TestMethod]
	public async Task DiscoverRepositoriesAsyncMissingDirectoryReturnsEmpty()
	{
		RepoService service = new(new Mock<IGitService>().Object, new Mock<IProcessService>().Object);
		string missing = Path.Combine(Path.GetTempPath(), $"ktsu_missing_{Guid.NewGuid():N}");
		AbsoluteDirectoryPath missingPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(missing);
		IReadOnlyList<string> result = await service.DiscoverRepositoriesAsync(missingPath).ConfigureAwait(false);
		Assert.AreEqual(0, result.Count);
	}

	[TestMethod]
	public async Task DiscoverRepositoriesAsyncFindsGitDirectories()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_repo_{Guid.NewGuid():N}");
		string repoA = Path.Combine(root, "repo-a");
		string repoB = Path.Combine(root, "nested", "repo-b");
		string nonRepo = Path.Combine(root, "plain");
		Directory.CreateDirectory(Path.Combine(repoA, ".git"));
		Directory.CreateDirectory(Path.Combine(repoB, ".git"));
		Directory.CreateDirectory(nonRepo);
		try
		{
			RepoService service = new(new Mock<IGitService>().Object, new Mock<IProcessService>().Object);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);
			IReadOnlyList<string> result = await service.DiscoverRepositoriesAsync(rootPath).ConfigureAwait(false);
			Assert.AreEqual(2, result.Count, "Should find exactly the two .git directories.");
			CollectionAssert.AreEquivalent(
				new[] { repoA, repoB },
				result.ToArray(),
				"Should return the two real repos, not the plain folder.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task DiscoverRepositoriesAsyncUpdatesCachedRepositoriesAndSolutions()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_cache_{Guid.NewGuid():N}");
		string repoPath = Path.Join(root, "repo-a");
		string nestedRepoPath = Path.Join(root, "nested", "repo-b");
		string solutionPath = Path.Join(repoPath, "RepoA.sln");
		Directory.CreateDirectory(Path.Join(repoPath, ".git"));
		Directory.CreateDirectory(Path.Join(nestedRepoPath, ".git"));
		await File.WriteAllTextAsync(solutionPath, string.Empty).ConfigureAwait(false);

		try
		{
			using RepoCacheSettings cache = new();
			Mock<ISettingsService> settings = new();
			settings.Setup(s => s.LoadOrCreate<RepoCacheSettings>()).Returns(cache);
			settings.Setup(s => s.SaveAsync(It.IsAny<RepoCacheSettings>())).Returns(Task.CompletedTask);

			RepoService service = new(new Mock<IGitService>().Object, new Mock<IProcessService>().Object, settings.Object);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			await service.DiscoverRepositoriesAsync(rootPath).ConfigureAwait(false);

			CollectionAssert.AreEquivalent(
				new[] { repoPath, nestedRepoPath },
				cache.Repositories.ToArray(),
				"Discover should refresh the cached repository list.");
			CollectionAssert.AreEquivalent(
				new[] { solutionPath },
				cache.Solutions.ToArray(),
				"Discover should refresh the cached solution list.");
			settings.Verify(s => s.SaveAsync(It.IsAny<RepoCacheSettings>()), Times.Once);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task ListAsyncReadsTheCacheWithoutWalkingTheFilesystem()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_list_cached_{Guid.NewGuid():N}");
		string cachedRepo = Path.Join(root, "repo-cached");
		string cachedSolution = Path.Join(cachedRepo, "Cached.sln");

		// On disk but absent from the cache: only a filesystem walk could turn this up.
		string undiscoveredRepo = Path.Join(root, "repo-on-disk-only");
		Directory.CreateDirectory(Path.Join(cachedRepo, ".git"));
		Directory.CreateDirectory(Path.Join(undiscoveredRepo, ".git"));
		await File.WriteAllTextAsync(cachedSolution, string.Empty).ConfigureAwait(false);

		try
		{
			using RepoCacheSettings cache = new()
			{
				Repositories = [cachedRepo],
				Solutions = [cachedSolution],
			};

			Mock<ISettingsService> settings = new();
			settings.Setup(s => s.LoadOrCreate<RepoCacheSettings>()).Returns(cache);
			settings.Setup(s => s.SaveAsync(It.IsAny<RepoCacheSettings>())).Returns(Task.CompletedTask);

			RepoService service = new(new Mock<IGitService>().Object, new Mock<IProcessService>().Object, settings.Object);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			int exit = await service.ListAsync(rootPath).ConfigureAwait(false);

			Assert.AreEqual(0, exit);
			CollectionAssert.AreEquivalent(
				new[] { cachedRepo },
				cache.Repositories.ToArray(),
				"Listing a populated cache must not re-walk the filesystem, so the repo only on disk should stay unlisted.");
			settings.Verify(
				s => s.SaveAsync(It.IsAny<RepoCacheSettings>()),
				Times.Never,
				"Reading the cache should not rewrite it.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task ListAsyncRefreshRewalksTheFilesystem()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_list_refresh_{Guid.NewGuid():N}");
		string cachedRepo = Path.Join(root, "repo-cached");
		string undiscoveredRepo = Path.Join(root, "repo-on-disk-only");
		Directory.CreateDirectory(Path.Join(cachedRepo, ".git"));
		Directory.CreateDirectory(Path.Join(undiscoveredRepo, ".git"));

		try
		{
			using RepoCacheSettings cache = new()
			{
				Repositories = [cachedRepo],
			};

			Mock<ISettingsService> settings = new();
			settings.Setup(s => s.LoadOrCreate<RepoCacheSettings>()).Returns(cache);
			settings.Setup(s => s.SaveAsync(It.IsAny<RepoCacheSettings>())).Returns(Task.CompletedTask);

			RepoService service = new(new Mock<IGitService>().Object, new Mock<IProcessService>().Object, settings.Object);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			int exit = await service.ListAsync(rootPath, refresh: true).ConfigureAwait(false);

			Assert.AreEqual(0, exit);
			CollectionAssert.AreEquivalent(
				new[] { cachedRepo, undiscoveredRepo },
				cache.Repositories.ToArray(),
				"--refresh should re-walk the filesystem and pick up the repository the cache had not seen.");
			settings.Verify(s => s.SaveAsync(It.IsAny<RepoCacheSettings>()), Times.Once);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task ListAsyncWalksWhenTheCacheIsEmpty()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_list_empty_{Guid.NewGuid():N}");
		string repo = Path.Join(root, "repo-a");
		Directory.CreateDirectory(Path.Join(repo, ".git"));

		try
		{
			using RepoCacheSettings cache = new();

			Mock<ISettingsService> settings = new();
			settings.Setup(s => s.LoadOrCreate<RepoCacheSettings>()).Returns(cache);
			settings.Setup(s => s.SaveAsync(It.IsAny<RepoCacheSettings>())).Returns(Task.CompletedTask);

			RepoService service = new(new Mock<IGitService>().Object, new Mock<IProcessService>().Object, settings.Object);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			int exit = await service.ListAsync(rootPath).ConfigureAwait(false);

			Assert.AreEqual(0, exit);
			CollectionAssert.AreEquivalent(
				new[] { repo },
				cache.Repositories.ToArray(),
				"An empty cache has nothing to list, so it should be populated by a walk.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task ListAsyncRefreshOfAMissingDirectoryFails()
	{
		using RepoCacheSettings cache = new()
		{
			Repositories = [Path.Join(Path.GetTempPath(), "repo-cached")],
		};

		Mock<ISettingsService> settings = new();
		settings.Setup(s => s.LoadOrCreate<RepoCacheSettings>()).Returns(cache);

		RepoService service = new(new Mock<IGitService>().Object, new Mock<IProcessService>().Object, settings.Object);
		string missing = Path.Join(Path.GetTempPath(), $"ktsu_list_missing_{Guid.NewGuid():N}");
		AbsoluteDirectoryPath missingPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(missing);

		int exit = await service.ListAsync(missingPath, refresh: true).ConfigureAwait(false);

		Assert.AreEqual(1, exit, "A refresh of a path that does not exist has nothing to walk.");
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ListAsyncJsonFormatWritesParseableJsonToStdout()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_list_json_{Guid.NewGuid():N}");
		string repo = Path.Join(root, "repo-a");
		string solution = Path.Join(repo, "A.sln");
		string orphan = Path.Join(root, "Loose.sln");
		Directory.CreateDirectory(repo);

		try
		{
			using RepoCacheSettings cache = new()
			{
				Repositories = [repo],
				Solutions = [solution, orphan],
			};

			Mock<ISettingsService> settings = new();
			settings.Setup(s => s.LoadOrCreate<RepoCacheSettings>()).Returns(cache);

			RepoService service = new(new Mock<IGitService>().Object, new Mock<IProcessService>().Object, settings.Object);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			string output = await CaptureConsoleAsync(
				() => service.ListAsync(rootPath, format: RepoListFormat.Json)).ConfigureAwait(false);

			using JsonDocument document = JsonDocument.Parse(output);
			JsonElement repositories = document.RootElement.GetProperty("repositories");

			Assert.AreEqual(1, repositories.GetArrayLength(), "JSON output should carry the one cached repository.");
			Assert.AreEqual("repo-a", repositories[0].GetProperty("name").GetString());
			Assert.AreEqual(repo, repositories[0].GetProperty("path").GetString());
			CollectionAssert.AreEquivalent(
				new[] { solution },
				repositories[0].GetProperty("solutions").EnumerateArray().Select(s => s.GetString()).ToArray());
			CollectionAssert.AreEquivalent(
				new[] { orphan },
				document.RootElement.GetProperty("orphanSolutions").EnumerateArray().Select(s => s.GetString()).ToArray());
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ListAsyncSaysSoWhenThereIsNothingToList()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_list_none_{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);

		try
		{
			using RepoCacheSettings cache = new();

			Mock<ISettingsService> settings = new();
			settings.Setup(s => s.LoadOrCreate<RepoCacheSettings>()).Returns(cache);
			settings.Setup(s => s.SaveAsync(It.IsAny<RepoCacheSettings>())).Returns(Task.CompletedTask);

			RepoService service = new(new Mock<IGitService>().Object, new Mock<IProcessService>().Object, settings.Object);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			string output = await CaptureConsoleAsync(() => service.ListAsync(rootPath)).ConfigureAwait(false);

			StringAssert.Contains(
				output,
				"No repositories cached",
				"An empty cache over an empty directory should say so rather than print a bare table.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ListAsyncReportsSolutionsOutsideEveryCachedRepository()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_list_orphan_{Guid.NewGuid():N}");
		string repo = Path.Join(root, "repo-a");
		string orphan = Path.Join(root, "Loose.sln");
		Directory.CreateDirectory(repo);

		try
		{
			using RepoCacheSettings cache = new()
			{
				Repositories = [repo],
				Solutions = [orphan],
			};

			Mock<ISettingsService> settings = new();
			settings.Setup(s => s.LoadOrCreate<RepoCacheSettings>()).Returns(cache);

			RepoService service = new(new Mock<IGitService>().Object, new Mock<IProcessService>().Object, settings.Object);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			string output = await CaptureConsoleAsync(() => service.ListAsync(rootPath)).ConfigureAwait(false);

			StringAssert.Contains(output, "repo-a", "The cached repository should still be listed.");
			StringAssert.Contains(
				output,
				"outside every cached repository",
				"A solution no repository contains should be reported, not dropped.");
			StringAssert.Contains(output, "Loose.sln", "The orphan solution should be named.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	private static Task<string> CaptureConsoleAsync(Func<Task> action) => ConsoleCapture.CaptureAsync(action);

	[TestMethod]
	public void GroupSolutionsByRepositoryIgnoresDuplicateCacheEntries()
	{
		string repo = Path.Join(Path.GetTempPath(), "ktsu_group_dupe", "repo-a");
		string solution = Path.Join(repo, "A.sln");

		RepositoryListingSet set = RepoService.GroupSolutionsByRepository([repo, repo], [solution]);

		Assert.AreEqual(1, set.Repositories.Count, "A repository listed twice in the cache should appear once.");
		CollectionAssert.AreEquivalent(new[] { solution }, set.Repositories.Single().Solutions.ToArray());
	}

	[TestMethod]
	public void GroupSolutionsByRepositoryPutsEachSolutionUnderItsRepository()
	{
		string root = Path.Join(Path.GetTempPath(), "ktsu_group");
		string repoA = Path.Join(root, "repo-a");
		string repoB = Path.Join(root, "repo-b");
		string solutionA1 = Path.Join(repoA, "A1.sln");
		string solutionA2 = Path.Join(repoA, "nested", "A2.sln");
		string solutionB = Path.Join(repoB, "B.sln");

		RepositoryListingSet set = RepoService.GroupSolutionsByRepository(
			[repoB, repoA],
			[solutionB, solutionA2, solutionA1]);

		CollectionAssert.AreEqual(
			RepoAThenRepoB,
			set.Repositories.Select(r => r.Name).ToArray(),
			"Repositories should be listed by name regardless of cache order.");
		CollectionAssert.AreEquivalent(
			new[] { solutionA1, solutionA2 },
			set.Repositories.Single(r => r.Name == "repo-a").Solutions.ToArray());
		CollectionAssert.AreEquivalent(
			new[] { solutionB },
			set.Repositories.Single(r => r.Name == "repo-b").Solutions.ToArray());
		Assert.AreEqual(0, set.OrphanSolutions.Count);
	}

	[TestMethod]
	public void GroupSolutionsByRepositoryPrefersTheNearestEnclosingRepository()
	{
		string outer = Path.Join(Path.GetTempPath(), "ktsu_group_outer");
		string inner = Path.Join(outer, "vendor", "inner");
		string solution = Path.Join(inner, "Inner.sln");

		RepositoryListingSet set = RepoService.GroupSolutionsByRepository([outer, inner], [solution]);

		Assert.AreEqual(0, set.Repositories.Single(r => r.Name == "ktsu_group_outer").Solutions.Count);
		CollectionAssert.AreEquivalent(
			new[] { solution },
			set.Repositories.Single(r => r.Name == "inner").Solutions.ToArray(),
			"A nested repository owns the solutions inside it, not the repository it sits in.");
	}

	[TestMethod]
	public void GroupSolutionsByRepositoryDoesNotClaimASiblingWithASharedPrefix()
	{
		string root = Path.Join(Path.GetTempPath(), "ktsu_group_prefix");
		string repo = Path.Join(root, "Repo");
		string sibling = Path.Join(root, "RepoTools");
		string solution = Path.Join(sibling, "RepoTools.sln");

		RepositoryListingSet set = RepoService.GroupSolutionsByRepository([repo], [solution]);

		Assert.AreEqual(0, set.Repositories.Single().Solutions.Count, "'Repo' must not claim a solution in 'RepoTools'.");
		CollectionAssert.AreEquivalent(new[] { solution }, set.OrphanSolutions.ToArray());
	}

	[TestMethod]
	public void GroupSolutionsByRepositoryReportsSolutionsNoRepositoryContains()
	{
		string root = Path.Join(Path.GetTempPath(), "ktsu_group_orphan");
		string repo = Path.Join(root, "repo-a");
		string loose = Path.Join(root, "Loose.sln");

		RepositoryListingSet set = RepoService.GroupSolutionsByRepository([repo], [loose]);

		Assert.AreEqual(0, set.Repositories.Single().Solutions.Count);
		CollectionAssert.AreEquivalent(new[] { loose }, set.OrphanSolutions.ToArray());
	}

	[TestMethod]
	public async Task ValidateCacheAsyncDryRunReportsStaleButDoesNotPrune()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_validate_dry_{Guid.NewGuid():N}");
		string liveRepo = Path.Join(root, "repo-live");
		string staleRepo = Path.Join(root, "repo-stale");
		string liveSolution = Path.Join(root, "Live.sln");
		string staleSolution = Path.Join(root, "Stale.sln");
		Directory.CreateDirectory(liveRepo);
		await File.WriteAllTextAsync(liveSolution, string.Empty).ConfigureAwait(false);

		try
		{
			using RepoCacheSettings cache = new()
			{
				Repositories = [liveRepo, staleRepo],
				Solutions = [liveSolution, staleSolution],
			};

			Mock<ISettingsService> settings = new();
			settings.Setup(s => s.LoadOrCreate<RepoCacheSettings>()).Returns(cache);
			settings.Setup(s => s.SaveAsync(It.IsAny<RepoCacheSettings>())).Returns(Task.CompletedTask);

			RepoService service = new(new Mock<IGitService>().Object, new Mock<IProcessService>().Object, settings.Object);

			int exit = await service.ValidateCacheAsync(dryRun: true).ConfigureAwait(false);

			Assert.AreEqual(0, exit);
			CollectionAssert.AreEquivalent(new[] { liveRepo, staleRepo }, cache.Repositories.ToArray());
			CollectionAssert.AreEquivalent(new[] { liveSolution, staleSolution }, cache.Solutions.ToArray());
			settings.Verify(s => s.SaveAsync(It.IsAny<RepoCacheSettings>()), Times.Never);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task ValidateCacheAsyncPrunesStaleEntriesAndPersists()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_validate_{Guid.NewGuid():N}");
		string liveRepo = Path.Join(root, "repo-live");
		string staleRepo = Path.Join(root, "repo-stale");
		string liveSolution = Path.Join(root, "Live.sln");
		string staleSolution = Path.Join(root, "Stale.sln");
		Directory.CreateDirectory(liveRepo);
		await File.WriteAllTextAsync(liveSolution, string.Empty).ConfigureAwait(false);

		try
		{
			using RepoCacheSettings cache = new()
			{
				Repositories = [liveRepo, staleRepo],
				Solutions = [liveSolution, staleSolution],
			};

			Mock<ISettingsService> settings = new();
			settings.Setup(s => s.LoadOrCreate<RepoCacheSettings>()).Returns(cache);
			settings.Setup(s => s.SaveAsync(It.IsAny<RepoCacheSettings>())).Returns(Task.CompletedTask);

			RepoService service = new(new Mock<IGitService>().Object, new Mock<IProcessService>().Object, settings.Object);

			int exit = await service.ValidateCacheAsync().ConfigureAwait(false);

			Assert.AreEqual(0, exit);
			CollectionAssert.AreEquivalent(new[] { liveRepo }, cache.Repositories.ToArray());
			CollectionAssert.AreEquivalent(new[] { liveSolution }, cache.Solutions.ToArray());
			settings.Verify(s => s.SaveAsync(It.IsAny<RepoCacheSettings>()), Times.Once);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task PullAllAsyncPullsEveryRepositoryAndReportsFailures()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_pull_{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Join(root, "good", ".git"));
		Directory.CreateDirectory(Path.Join(root, "bad", ".git"));
		try
		{
			RecordingPullProcessService fake = new(failInDirNamed: "bad");
			RepoService service = new(new Mock<IGitService>().Object, fake);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			int exit = await service.PullAllAsync(rootPath).ConfigureAwait(false);

			Assert.AreEqual(1, exit, "A failed pull should surface in the aggregate exit code.");
			Assert.AreEqual(2, fake.Calls.Count, "Both repositories should be pulled.");
			Assert.IsTrue(
				fake.Calls.All(c => c.Command == "git" && c.Arguments.StartsWith("pull", StringComparison.Ordinal)),
				"Each repository should be pulled with git pull.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task FetchAllAsyncFetchesEveryRepositoryWithoutTouchingWorkingTrees()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_fetch_{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Join(root, "alpha", ".git"));
		Directory.CreateDirectory(Path.Join(root, "beta", ".git"));
		try
		{
			RecordingFetchProcessService fake = new();
			RepoService service = new(new Mock<IGitService>().Object, fake);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			int exit = await service.FetchAllAsync(rootPath, parallel: false).ConfigureAwait(false);

			Assert.AreEqual(0, exit, "Every repository fetched cleanly, so the exit code should be zero.");

			List<string> fetchArguments = [.. fake.Calls
				.Where(c => c.Arguments.StartsWith("fetch", StringComparison.Ordinal))
				.Select(c => c.Arguments)];

			Assert.AreEqual(2, fetchArguments.Count, "Both repositories should be fetched.");
			Assert.IsTrue(
				fake.Calls.All(c => c.Command == "git"),
				"Only git should be invoked.");
			Assert.IsFalse(
				fake.Calls.Any(c =>
					c.Arguments.StartsWith("pull", StringComparison.Ordinal) ||
					c.Arguments.Contains("merge", StringComparison.Ordinal) ||
					c.Arguments.Contains("autostash", StringComparison.Ordinal)),
				"Fetch must not merge, pull, or autostash — that is what makes it safe over dirty working trees.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task FetchAllAsyncReportsFailuresInTheExitCode()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_fetchfail_{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Join(root, "good", ".git"));
		Directory.CreateDirectory(Path.Join(root, "bad", ".git"));
		try
		{
			RecordingFetchProcessService fake = new(failInDirNamed: "bad");
			RepoService service = new(new Mock<IGitService>().Object, fake);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			int exit = await service.FetchAllAsync(rootPath, parallel: false).ConfigureAwait(false);

			Assert.AreEqual(1, exit, "A failed fetch should surface in the aggregate exit code.");
			Assert.IsFalse(
				fake.Calls.Any(c =>
					c.Arguments.StartsWith("rev-list", StringComparison.Ordinal) &&
					Path.GetFileName(c.WorkingDirectory) == "bad"),
				"A repository whose fetch failed has nothing worth counting against its upstream.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task FetchAllAsyncCountsDivergenceAgainstTheUpstream()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_fetchcount_{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Join(root, "solo", ".git"));
		try
		{
			RecordingFetchProcessService fake = new(aheadBehind: "2\t5");
			RepoService service = new(new Mock<IGitService>().Object, fake);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			await service.FetchAllAsync(rootPath, parallel: false).ConfigureAwait(false);

			string? revList = fake.Calls
				.Select(c => c.Arguments)
				.FirstOrDefault(a => a.StartsWith("rev-list", StringComparison.Ordinal));

			Assert.IsNotNull(revList, "Ahead/behind should be counted after a successful fetch.");
			Assert.IsTrue(
				revList.Contains("--left-right", StringComparison.Ordinal) &&
				revList.Contains("--count", StringComparison.Ordinal) &&
				revList.Contains("HEAD...@{upstream}", StringComparison.Ordinal),
				$"Counting should compare HEAD against its upstream, but ran '{revList}'.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public void AheadBehindParseReadsTheTabSeparatedCounts()
	{
		AheadBehind? parsed = AheadBehind.Parse(["3\t7"]);

		Assert.AreEqual<AheadBehind?>(
			new AheadBehind(3, 7),
			parsed,
			"The left count is how far ahead the branch is, the right how far behind.");
	}

	[TestMethod]
	public void AheadBehindParseReturnsNullForOutputThatIsNotTwoCounts()
	{
		Assert.IsNull(AheadBehind.Parse(null), "No output means no upstream to compare against.");
		Assert.IsNull(AheadBehind.Parse([]), "No output means no upstream to compare against.");
		Assert.IsNull(AheadBehind.Parse(["fatal: no upstream configured for branch 'main'"]));
		Assert.IsNull(AheadBehind.Parse(["3"]), "One count is not a divergence.");
	}

	[TestMethod]
	public void AheadBehindRenderShowsArrowsSyncAndNoUpstream()
	{
		Assert.AreEqual("↑2 ↓5", AheadBehind.Render(new AheadBehind(2, 5)));
		Assert.AreEqual("↑2", AheadBehind.Render(new AheadBehind(2, 0)), "A zero side should be dropped.");
		Assert.AreEqual("↓5", AheadBehind.Render(new AheadBehind(0, 5)), "A zero side should be dropped.");
		Assert.AreEqual("≡", AheadBehind.Render(new AheadBehind(0, 0)));
		Assert.AreEqual("—", AheadBehind.Render(null));
	}

	[TestMethod]
	public async Task BuildAndTestAsyncParallelAggregatesExitCodes()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_par_{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		try
		{
			// Two top-level solutions in separate subdirs.
			string slnAPath = Path.Combine(root, "a", "A.sln");
			string slnBPath = Path.Combine(root, "b", "B.sln");
			Directory.CreateDirectory(Path.GetDirectoryName(slnAPath)!);
			Directory.CreateDirectory(Path.GetDirectoryName(slnBPath)!);
			await File.WriteAllTextAsync(slnAPath, string.Empty).ConfigureAwait(false);
			await File.WriteAllTextAsync(slnBPath, string.Empty).ConfigureAwait(false);

			DelayedFakeProcessService fake = new(delayMs: 50, failBuildInDirNamed: "b");
			RepoService service = new(new Mock<IGitService>().Object, fake);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			int exit = await service.BuildAndTestAsync(rootPath, parallel: true).ConfigureAwait(false);

			Assert.AreEqual(1, exit, "Aggregated exit code should be non-zero when any solution fails.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	/// <remarks>
	/// Asserts on how many solutions were in flight at once rather than on elapsed time. The wall-clock
	/// version of this test was a coin flip: BuildAndTestAsync runs at ProcessorCount / 2, which is 1 on
	/// a three-core runner, so there the "parallel" run is sequential and the two timings differ only by
	/// scheduling noise.
	/// </remarks>
	[TestMethod]
	public async Task BuildAndTestAsyncOverlapsSolutionsOnlyWhenParallel()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_parspeed_{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		try
		{
			for (int i = 0; i < 4; i++)
			{
				string dir = Path.Combine(root, $"s{i}");
				Directory.CreateDirectory(dir);
				await File.WriteAllTextAsync(Path.Combine(dir, $"S{i}.sln"), string.Empty).ConfigureAwait(false);
			}

			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			DelayedFakeProcessService fakeSeq = new(delayMs: 20);
			RepoService seqService = new(new Mock<IGitService>().Object, fakeSeq);
			await seqService.BuildAndTestAsync(rootPath, parallel: false).ConfigureAwait(false);

			DelayedFakeProcessService fakePar = new(delayMs: 20);
			RepoService parService = new(new Mock<IGitService>().Object, fakePar);
			await parService.BuildAndTestAsync(rootPath, parallel: true).ConfigureAwait(false);

			Assert.AreEqual(1, fakeSeq.MaxConcurrent, "A sequential run should never have two solutions in flight.");

			// The same degree of parallelism BuildAndTestAsync picks.
			int degreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2);

			Assert.IsTrue(
				fakePar.MaxConcurrent <= degreeOfParallelism,
				$"A parallel run should stay within its degree of parallelism ({degreeOfParallelism}), but reached {fakePar.MaxConcurrent}.");

			if (degreeOfParallelism == 1)
			{
				Assert.AreEqual(
					1,
					fakePar.MaxConcurrent,
					"This runner has too few cores for BuildAndTestAsync to overlap anything, so a parallel run is a sequential one.");
			}
			else
			{
				Assert.IsTrue(
					fakePar.MaxConcurrent > 1,
					$"A parallel run on {Environment.ProcessorCount} cores should overlap solutions, but never had more than one in flight.");
			}
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task InstallLfsAsyncConfiguresEveryRepositoryLocally()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_lfs_{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Join(root, "alpha", ".git"));
		Directory.CreateDirectory(Path.Join(root, "beta", ".git"));
		try
		{
			RecordingLfsProcessService fake = new();
			RepoService service = new(new Mock<IGitService>().Object, fake);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			int exit = await service.InstallLfsAsync(rootPath).ConfigureAwait(false);

			Assert.AreEqual(0, exit, "Every repository was configured, so the exit code should be zero.");

			List<string> installDirectories = [.. fake.Calls
				.Where(c => c.Arguments.StartsWith("lfs install", StringComparison.Ordinal))
				.Select(c => Path.GetFileName(c.WorkingDirectory!))];

			Assert.AreEqual(2, installDirectories.Count, "Both repositories should be configured.");
			CollectionAssert.AreEquivalent(
				AlphaThenBeta,
				installDirectories,
				"Each discovered repository should be configured in its own working directory.");
			Assert.IsTrue(
				fake.Calls.All(c => c.Command == "git"),
				"Only git should be invoked.");
			Assert.IsTrue(
				fake.Calls
					.Where(c => c.Arguments.StartsWith("lfs install", StringComparison.Ordinal))
					.All(c => c.Arguments.Contains("--local", StringComparison.Ordinal)),
				"--local keeps the filters in the repository's own config instead of the user's global one.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task InstallLfsAsyncReportsMissingGitLfsWithoutFailingTheRun()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_lfsmissing_{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Join(root, "alpha", ".git"));
		try
		{
			RecordingLfsProcessService fake = new(
				installExitCode: 1,
				installErrors: ["git: 'lfs' is not a git command. See 'git --help'."]);
			RepoService service = new(new Mock<IGitService>().Object, fake);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			int exit = await service.InstallLfsAsync(rootPath).ConfigureAwait(false);

			Assert.AreEqual(0, exit, "A missing git lfs is one absent tool, not a broken repository, so it is reported rather than failed.");
			Assert.IsFalse(
				fake.Calls.Any(c => c.Arguments.Contains("ls-files", StringComparison.Ordinal)),
				"Listing tracked files needs the same missing subcommand, so it should not be attempted.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task InstallLfsAsyncFailsWhenAnInstallFailsForAnyOtherReason()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_lfsfail_{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Join(root, "alpha", ".git"));
		try
		{
			RecordingLfsProcessService fake = new(
				installExitCode: 1,
				installErrors: ["error: could not lock config file .git/config: Permission denied"]);
			RepoService service = new(new Mock<IGitService>().Object, fake);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			int exit = await service.InstallLfsAsync(rootPath).ConfigureAwait(false);

			Assert.AreEqual(1, exit, "An install that failed for a reason other than a missing git lfs should surface in the exit code.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task InstallLfsAsyncCountsPointerFilesLeftInTheWorkingTree()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_lfspointers_{Guid.NewGuid():N}");
		string repo = Path.Join(root, "alpha");
		Directory.CreateDirectory(Path.Join(repo, ".git"));
		try
		{
			await File.WriteAllTextAsync(
				Path.Join(repo, "icon.png"),
				"version https://git-lfs.github.com/spec/v1\noid sha256:0a1b2c3d\nsize 4096\n").ConfigureAwait(false);

			RecordingLfsProcessService fake = new(trackedFiles: ["icon.png"]);
			RepoService service = new(new Mock<IGitService>().Object, fake);
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);

			int exit = await service.InstallLfsAsync(rootPath).ConfigureAwait(false);

			Assert.AreEqual(
				0,
				exit,
				"A working tree that still holds pointer files needs a checkout, which is worth reporting but is not an install failure.");
			Assert.IsTrue(
				fake.Calls.Any(c => c.Arguments.StartsWith("lfs ls-files", StringComparison.Ordinal)),
				"A successful install should be followed by a listing of what LFS tracks, so the pointers can be counted.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task FetchAllAsyncMissingDirectoryReturnsFailure()
	{
		RepoService service = new(new Mock<IGitService>().Object, new RecordingFetchProcessService());
		string missing = Path.Join(Path.GetTempPath(), $"ktsu_fetchmissingdir_{Guid.NewGuid():N}");
		AbsoluteDirectoryPath missingPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(missing);

		int exit = await service.FetchAllAsync(missingPath, parallel: false).ConfigureAwait(false);

		Assert.AreEqual(1, exit, "A path that does not exist is a caller error, not an empty workspace.");
	}

	[TestMethod]
	public async Task InstallLfsAsyncMissingDirectoryReturnsFailure()
	{
		RepoService service = new(new Mock<IGitService>().Object, new RecordingLfsProcessService());
		string missing = Path.Join(Path.GetTempPath(), $"ktsu_lfsmissingdir_{Guid.NewGuid():N}");
		AbsoluteDirectoryPath missingPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(missing);

		int exit = await service.InstallLfsAsync(missingPath).ConfigureAwait(false);

		Assert.AreEqual(1, exit, "A path that does not exist is a caller error, not an empty workspace.");
	}

	[TestMethod]
	public async Task CountUnsmudgedPointersCountsOnlyPointerFiles()
	{
		string repo = Path.Join(Path.GetTempPath(), $"ktsu_lfspointer_{Guid.NewGuid():N}");
		Directory.CreateDirectory(repo);
		try
		{
			string pointer = Path.Join(repo, "icon.png");
			string real = Path.Join(repo, "logo.png");

			await File.WriteAllTextAsync(
				pointer,
				"version https://git-lfs.github.com/spec/v1\noid sha256:0a1b2c3d\nsize 4096\n").ConfigureAwait(false);
			await File.WriteAllTextAsync(real, new string('x', 2048)).ConfigureAwait(false);

			int count = RepoService.CountUnsmudgedPointers(repo, ["icon.png", "logo.png", "deleted.png", "  "]);

			Assert.AreEqual(
				1,
				count,
				"Only the file whose content is still a pointer should be counted; real content, a missing path and a blank line should not.");
		}
		finally
		{
			Directory.Delete(repo, recursive: true);
		}
	}

	private sealed class RecordingLfsProcessService(
		int installExitCode = 0,
		IReadOnlyList<string>? installErrors = null,
		IReadOnlyList<string>? trackedFiles = null) : IProcessService
	{
		public List<(string Command, string Arguments, string? WorkingDirectory)> Calls { get; } = [];

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			RunAsync(command, arguments, workingDirectory, null, ct);

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default)
		{
			Calls.Add((command, arguments, workingDirectory));

			return Task.FromResult(arguments.StartsWith("lfs ls-files", StringComparison.Ordinal)
				? new ProcessResult(0, trackedFiles ?? [], [])
				: new ProcessResult(installExitCode, [], installErrors ?? []));
		}
	}

	private sealed class RecordingPullProcessService(string failInDirNamed) : IProcessService
	{
		public List<(string Command, string Arguments, string? WorkingDirectory)> Calls { get; } = [];

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			RunAsync(command, arguments, workingDirectory, null, ct);

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default)
		{
			Calls.Add((command, arguments, workingDirectory));

			bool fails = workingDirectory is not null &&
				string.Equals(Path.GetFileName(workingDirectory), failInDirNamed, StringComparison.OrdinalIgnoreCase);

			return Task.FromResult(fails
				? new ProcessResult(1, [], ["fatal: could not read from remote repository"])
				: new ProcessResult(0, [], []));
		}
	}

	private sealed class RecordingFetchProcessService(string? failInDirNamed = null, string aheadBehind = "0\t0") : IProcessService
	{
		public List<(string Command, string Arguments, string? WorkingDirectory)> Calls { get; } = [];

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			RunAsync(command, arguments, workingDirectory, null, ct);

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default)
		{
			Calls.Add((command, arguments, workingDirectory));

			bool fails = failInDirNamed is not null &&
				workingDirectory is not null &&
				string.Equals(Path.GetFileName(workingDirectory), failInDirNamed, StringComparison.OrdinalIgnoreCase);

			if (fails)
			{
				return Task.FromResult(new ProcessResult(1, [], ["fatal: could not read from remote repository"]));
			}

			return Task.FromResult(arguments.StartsWith("rev-list", StringComparison.Ordinal)
				? new ProcessResult(0, [aheadBehind], [])
				: new ProcessResult(0, [], []));
		}
	}

	private sealed class DelayedFakeProcessService(int delayMs, string? failBuildInDirNamed = null) : IProcessService
	{
		private int inFlight;
		private int maxConcurrent;

		/// <summary>Gets the highest number of calls that were in flight at the same time.</summary>
		public int MaxConcurrent => Volatile.Read(ref maxConcurrent);

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			RunAsync(command, arguments, workingDirectory, null, ct);

		public async Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default)
		{
			int current = Interlocked.Increment(ref inFlight);

			// Raise the high-water mark without losing a concurrent update.
			int observed = Volatile.Read(ref maxConcurrent);
			while (current > observed)
			{
				int previous = Interlocked.CompareExchange(ref maxConcurrent, current, observed);
				if (previous == observed)
				{
					break;
				}

				observed = previous;
			}

			try
			{
				return await RunCoreAsync(arguments, workingDirectory, ct).ConfigureAwait(false);
			}
			finally
			{
				_ = Interlocked.Decrement(ref inFlight);
			}
		}

		private async Task<ProcessResult> RunCoreAsync(string arguments, string? workingDirectory, CancellationToken ct)
		{
			await Task.Delay(delayMs, ct).ConfigureAwait(false);

			int exit = 0;
			if (failBuildInDirNamed is not null &&
				arguments.StartsWith("build", StringComparison.Ordinal) &&
				workingDirectory is not null &&
				string.Equals(Path.GetFileName(workingDirectory), failBuildInDirNamed, StringComparison.OrdinalIgnoreCase))
			{
				exit = 1;
			}

			return new ProcessResult(exit, [], []);
		}
	}
}
