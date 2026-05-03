// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Test;

using System.IO;
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
		IReadOnlyList<string> result = await service.DiscoverRepositoriesAsync(missing).ConfigureAwait(false);
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
			IReadOnlyList<string> result = await service.DiscoverRepositoriesAsync(root).ConfigureAwait(false);
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
}
