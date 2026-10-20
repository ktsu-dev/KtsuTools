// Copyright (c) 2023-2026 ktsu-dev contributors

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

	[TestMethod]
	[DataRow(BlockChoice.UseVersion1, "foo=1")]
	[DataRow(BlockChoice.UseVersion2, "foo=2")]
	[DataRow(BlockChoice.UseBoth, "foo=1", "foo=2")]
	[DataRow(BlockChoice.Skip, "foo=1")]
	public void ApplyBlockChoiceResolvesASingleLineConflict(BlockChoice choice, params string[] expected)
	{
		List<string> merged = ["line 4"];

		MergeService.ApplyBlockChoice(merged, ["foo=1"], ["foo=2"], choice);

		CollectionAssert.AreEqual(new List<string> { "line 4" }.Concat(expected).ToList(), merged, $"{choice} resolved the block wrongly.");
	}

	[TestMethod]
	public async Task InteractiveMergeKeepsASkippedLine()
	{
		const string version1 = "line 1\nline 2\nline 3\nline 4\nfoo=1\nline 6";
		const string version2 = "line 1\nline 2\nline 3\nline 4\nfoo=2\nline 6";
		string merged = string.Empty;

		await ConsoleCapture.CaptureAsync(() =>
		{
			merged = MergeService.InteractiveMerge(version1, version2, (_, _) => BlockChoice.Skip);
			return Task.CompletedTask;
		}).ConfigureAwait(false);

		Assert.AreEqual(version1, merged, "Skipping the foo= block must leave line 5 as version 1 has it.");
	}

	[TestMethod]
	public void DiffStyleParserAcceptsCanonicalNames()
	{
		Assert.IsTrue(DiffStyleParser.TryParse("side-by-side", out DiffStyle side));
		Assert.AreEqual(DiffStyle.SideBySide, side);

		Assert.IsTrue(DiffStyleParser.TryParse("git", out DiffStyle git));
		Assert.AreEqual(DiffStyle.Git, git);
	}

	[TestMethod]
	public void DiffStyleParserDefaultsForNullOrEmpty()
	{
		Assert.IsTrue(DiffStyleParser.TryParse(null, out DiffStyle a));
		Assert.AreEqual(DiffStyle.SideBySide, a);

		Assert.IsTrue(DiffStyleParser.TryParse("  ", out DiffStyle b));
		Assert.AreEqual(DiffStyle.SideBySide, b);
	}

	[TestMethod]
	public void DiffStyleParserRejectsUnknownValues()
	{
		Assert.IsFalse(DiffStyleParser.TryParse("rainbow", out DiffStyle _));
		Assert.IsFalse(DiffStyleParser.TryParse("ColoredDeluxe", out DiffStyle _));
	}

	[TestMethod]
	public void DiffStyleParserRoundTripsCanonicalForm()
	{
		Assert.AreEqual("side-by-side", DiffStyleParser.ToCanonicalString(DiffStyle.SideBySide));
		Assert.AreEqual("git", DiffStyleParser.ToCanonicalString(DiffStyle.Git));
	}
}
