// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Test;

using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Sync;

[TestClass]
public class SyncServiceTests
{
	[TestMethod]
	public void HashToStringEmptyArrayReturnsEmpty()
	{
		string result = SyncService.HashToString([]);
		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void HashToStringSingleByteReturnsTwoUppercaseHexChars()
	{
		Assert.AreEqual("0F", SyncService.HashToString([0x0F]));
		Assert.AreEqual("FF", SyncService.HashToString([0xFF]));
		Assert.AreEqual("00", SyncService.HashToString([0x00]));
	}

	[TestMethod]
	public void HashToStringKnownBytesProducesExpectedHex()
	{
		byte[] bytes = [0xDE, 0xAD, 0xBE, 0xEF];
		Assert.AreEqual("DEADBEEF", SyncService.HashToString(bytes));
	}

	[TestMethod]
	public void IsRepoNestedNoGitInChainReturnsFalse()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_nested_{Guid.NewGuid():N}");
		string leaf = Path.Combine(root, "a", "b", "c");
		Directory.CreateDirectory(leaf);
		try
		{
			AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(leaf);
			Assert.IsFalse(SyncService.IsRepoNested(path));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public void IsRepoNestedSingleRepoAncestorReturnsFalse()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_nested_{Guid.NewGuid():N}");
		string repoRoot = Path.Combine(root, "repo");
		string leaf = Path.Combine(repoRoot, "src");
		Directory.CreateDirectory(leaf);
		Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
		try
		{
			AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(leaf);
			Assert.IsFalse(SyncService.IsRepoNested(path));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public void IsRepoNestedTwoRepoAncestorsReturnsTrue()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_nested_{Guid.NewGuid():N}");
		string outerRepo = Path.Combine(root, "outer");
		string innerRepo = Path.Combine(outerRepo, "inner");
		string leaf = Path.Combine(innerRepo, "src");
		Directory.CreateDirectory(leaf);
		Directory.CreateDirectory(Path.Combine(outerRepo, ".git"));
		Directory.CreateDirectory(Path.Combine(innerRepo, ".git"));
		try
		{
			AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(leaf);
			Assert.IsTrue(SyncService.IsRepoNested(path));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}
}
