// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Semantics.Paths;
using KtsuTools.Core.Services.Git;
using KtsuTools.Core.Services.Process;
using KtsuTools.Repo;
using Moq;

[TestClass]
public class RepoServiceTests
{
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

	[TestMethod]
	public async Task BuildAndTestAsyncParallelIsFasterThanSequential()
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

			DelayedFakeProcessService fakeSeq = new(delayMs: 120);
			RepoService seqService = new(new Mock<IGitService>().Object, fakeSeq);
			Stopwatch swSeq = Stopwatch.StartNew();
			await seqService.BuildAndTestAsync(rootPath, parallel: false).ConfigureAwait(false);
			swSeq.Stop();

			DelayedFakeProcessService fakePar = new(delayMs: 120);
			RepoService parService = new(new Mock<IGitService>().Object, fakePar);
			Stopwatch swPar = Stopwatch.StartNew();
			await parService.BuildAndTestAsync(rootPath, parallel: true).ConfigureAwait(false);
			swPar.Stop();

			Assert.IsTrue(
				swPar.ElapsedMilliseconds < swSeq.ElapsedMilliseconds,
				$"Parallel ({swPar.ElapsedMilliseconds} ms) should be faster than sequential ({swSeq.ElapsedMilliseconds} ms).");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
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
		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			RunAsync(command, arguments, workingDirectory, null, ct);

		public async Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default)
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
