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
