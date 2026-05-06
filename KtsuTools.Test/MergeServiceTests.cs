// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Test;

using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Merge;

[TestClass]
public class MergeServiceTests
{
	[TestMethod]
	public async Task RunMergeAsyncMissingDirectoryReturnsErrorCode()
	{
		MergeService service = new();
		string missing = Path.Combine(Path.GetTempPath(), $"ktsu_missing_{Guid.NewGuid():N}");
		AbsoluteDirectoryPath missingPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(missing);
		int result = await service.RunMergeAsync(missingPath, "*.txt").ConfigureAwait(false);
		Assert.AreEqual(1, result);
	}

	[TestMethod]
	public async Task RunMergeAsyncFewerThanTwoMatchesReturnsZeroWithoutMerging()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_merge_{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		try
		{
			await File.WriteAllTextAsync(Path.Combine(root, "only.txt"), "hello").ConfigureAwait(false);
			MergeService service = new();
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);
			int result = await service.RunMergeAsync(rootPath, "only.txt").ConfigureAwait(false);
			Assert.AreEqual(0, result, "Expected success exit code when there's nothing to merge.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task RunMergeAsyncAllMatchesIdenticalReturnsZeroWithoutPrompting()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_merge_{Guid.NewGuid():N}");
		string subA = Path.Combine(root, "a");
		string subB = Path.Combine(root, "b");
		Directory.CreateDirectory(subA);
		Directory.CreateDirectory(subB);
		try
		{
			await File.WriteAllTextAsync(Path.Combine(subA, "shared.txt"), "same content").ConfigureAwait(false);
			await File.WriteAllTextAsync(Path.Combine(subB, "shared.txt"), "same content").ConfigureAwait(false);
			MergeService service = new();
			AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);
			int result = await service.RunMergeAsync(rootPath, "shared.txt").ConfigureAwait(false);
			Assert.AreEqual(0, result, "Identical files should hash into one group and exit cleanly.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}
}
