// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.IO;
using System.Text;
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
	public async Task RunMergeAsyncWithAPatternMergesOnlyCopiesOfTheSameFile()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_merge_{Guid.NewGuid():N}");
		string subA = Path.Join(root, "a");
		string subB = Path.Join(root, "b");
		Directory.CreateDirectory(subA);
		Directory.CreateDirectory(subB);
		try
		{
			const string ciA = "name: CI\non: push\n";
			const string ciB = "name: CI\non: pull_request\n";
			const string dependabot = "version: 2\nupdates: []\n";
			await File.WriteAllTextAsync(Path.Join(subA, "ci.yml"), ciA).ConfigureAwait(false);
			await File.WriteAllTextAsync(Path.Join(subB, "ci.yml"), ciB).ConfigureAwait(false);
			await File.WriteAllTextAsync(Path.Join(subA, "dependabot.yml"), dependabot).ConfigureAwait(false);
			List<(string[] Version1, string[] Version2)> blocks = [];
			int result = 0;

			await ConsoleCapture.CaptureAsync(async () =>
			{
				AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);
				result = await MergeService.RunMergeAsync(rootPath, "*.yml", DiffStyle.Git, (version1, version2) =>
				{
					blocks.Add((version1, version2));
					return BlockChoice.UseVersion2;
				}, CancellationToken.None).ConfigureAwait(false);
			}).ConfigureAwait(false);

			Assert.AreEqual(0, result);
			Assert.AreEqual(dependabot, await File.ReadAllTextAsync(Path.Join(subA, "dependabot.yml")).ConfigureAwait(false), "dependabot.yml has no other copy, so it must be left alone.");
			string mergedA = await File.ReadAllTextAsync(Path.Join(subA, "ci.yml")).ConfigureAwait(false);
			string mergedB = await File.ReadAllTextAsync(Path.Join(subB, "ci.yml")).ConfigureAwait(false);
			Assert.AreEqual(mergedA, mergedB, "The two ci.yml copies should be merged into one.");
			Assert.IsTrue(mergedA.StartsWith("name: CI\n", StringComparison.Ordinal), $"ci.yml should only ever be merged with ci.yml, but became: {mergedA}");
			Assert.IsFalse(blocks.Any(b => b.Version1.Concat(b.Version2).Any(line => line.Contains("updates", StringComparison.Ordinal))), "A dependabot.yml line was offered in a ci.yml merge.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task RunMergeAsyncKeepsEachFilesByteOrderMark()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_merge_{Guid.NewGuid():N}");
		string subA = Path.Join(root, "a");
		string subB = Path.Join(root, "b");
		Directory.CreateDirectory(subA);
		Directory.CreateDirectory(subB);
		try
		{
			byte[] bom = [0xEF, 0xBB, 0xBF];
			string fileA = Path.Join(subA, "shared.cs");
			string fileB = Path.Join(subB, "shared.cs");
			await File.WriteAllBytesAsync(fileA, [.. bom, .. Encoding.UTF8.GetBytes("line 1\nfoo=1\nline 3")]).ConfigureAwait(false);
			await File.WriteAllBytesAsync(fileB, [.. bom, .. Encoding.UTF8.GetBytes("line 1\nfoo=2\nline 3")]).ConfigureAwait(false);

			int result = await RunMergeAsync(root, "shared.cs", BlockChoice.UseVersion2, BlockChoice.UseVersion1).ConfigureAwait(false);

			Assert.AreEqual(0, result);
			byte[] expected = [.. bom, .. Encoding.UTF8.GetBytes("line 1\nfoo=2\nline 3")];
			CollectionAssert.AreEqual(expected, await File.ReadAllBytesAsync(fileA).ConfigureAwait(false), "The merged file lost its BOM.");
			CollectionAssert.AreEqual(expected, await File.ReadAllBytesAsync(fileB).ConfigureAwait(false), "The merged file lost its BOM.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task RunMergeAsyncWritesALegacyFileBackByteForByteWhenItsVersionIsChosen()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_merge_{Guid.NewGuid():N}");
		string subA = Path.Join(root, "a");
		string subB = Path.Join(root, "b");
		Directory.CreateDirectory(subA);
		Directory.CreateDirectory(subB);
		try
		{
			// "café=1" in Windows-1252: 0xE9 is 'é', and is not valid UTF-8 on its own.
			byte[] legacy = [.. "line 1\ncaf"u8, 0xE9, .. "=1\nline 3"u8];
			string fileA = Path.Join(subA, "shared.txt");
			string fileB = Path.Join(subB, "shared.txt");
			await File.WriteAllBytesAsync(fileA, legacy).ConfigureAwait(false);
			await File.WriteAllBytesAsync(fileB, legacy).ConfigureAwait(false);
			string fileC = Path.Join(root, "shared.txt");
			await File.WriteAllTextAsync(fileC, "line 1\ncafé=2\nline 3").ConfigureAwait(false);

			int result = await RunMergeAsync(root, "shared.txt", BlockChoice.UseVersion1, BlockChoice.UseVersion2).ConfigureAwait(false);

			Assert.AreEqual(0, result);
			CollectionAssert.AreEqual(legacy, await File.ReadAllBytesAsync(fileA).ConfigureAwait(false), "The chosen legacy file must round-trip byte-for-byte, not gain U+FFFD.");
			CollectionAssert.AreEqual(legacy, await File.ReadAllBytesAsync(fileB).ConfigureAwait(false), "The chosen legacy file must round-trip byte-for-byte, not gain U+FFFD.");
			StringAssert.Contains(await File.ReadAllTextAsync(fileC).ConfigureAwait(false), "café=", "The UTF-8 copy must hold é as UTF-8.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task RunMergeAsyncTellsFilesApartThatDifferOnlyByByteOrderMark()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_merge_{Guid.NewGuid():N}");
		string subA = Path.Join(root, "a");
		string subB = Path.Join(root, "b");
		Directory.CreateDirectory(subA);
		Directory.CreateDirectory(subB);
		try
		{
			await File.WriteAllBytesAsync(Path.Join(subA, "shared.txt"), [0xEF, 0xBB, 0xBF, .. "same"u8]).ConfigureAwait(false);
			await File.WriteAllBytesAsync(Path.Join(subB, "shared.txt"), "same"u8.ToArray()).ConfigureAwait(false);

			string output = await ConsoleCapture.CaptureAsync(async () =>
				await MergeService.RunMergeAsync(
					AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root), "shared.txt", DiffStyle.SideBySide, (_, _) => BlockChoice.UseVersion1, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

			StringAssert.Contains(output, "2 unique version(s) found", "A BOM difference must not be hidden by hashing decoded text.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task RunMergeAsyncRefusesToWriteTextALegacyFileCantHold()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_merge_{Guid.NewGuid():N}");
		string subA = Path.Join(root, "a");
		Directory.CreateDirectory(subA);
		try
		{
			byte[] legacy = [.. "name=caf"u8, 0xE9];
			string fileA = Path.Join(subA, "shared.txt");
			string fileB = Path.Join(root, "shared.txt");
			await File.WriteAllBytesAsync(fileA, legacy).ConfigureAwait(false);
			await File.WriteAllTextAsync(fileB, "name=日本").ConfigureAwait(false);
			byte[] utf8Before = await File.ReadAllBytesAsync(fileB).ConfigureAwait(false);

			int result = await RunMergeAsync(root, "shared.txt", BlockChoice.UseVersion2, BlockChoice.UseVersion1).ConfigureAwait(false);

			Assert.AreEqual(1, result, "Latin-1 can't hold 日本, so the merge must stop.");
			CollectionAssert.AreEqual(legacy, await File.ReadAllBytesAsync(fileA).ConfigureAwait(false), "The legacy file must be left untouched.");
			CollectionAssert.AreEqual(utf8Before, await File.ReadAllBytesAsync(fileB).ConfigureAwait(false), "No file may be written in a refused round.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>
	/// Runs a merge with every conflicting block resolved as <paramref name="choice"/>. Which file is
	/// version 1 depends on the pair order, so <paramref name="otherwise"/> is used when the pair
	/// comes the other way round: the choice is applied to the block whose version 1 contains the
	/// text of the file under subdirectory <c>a</c>.
	/// </summary>
	private static async Task<int> RunMergeAsync(string root, string pattern, BlockChoice choice, BlockChoice? otherwise = null)
	{
		string aContent = EncodedText.Decode(await File.ReadAllBytesAsync(Path.Join(root, "a", pattern)).ConfigureAwait(false)).Text;
		int result = 0;
		await ConsoleCapture.CaptureAsync(async () =>
			result = await MergeService.RunMergeAsync(
				AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root),
				pattern,
				DiffStyle.SideBySide,
				(version1, _) => otherwise is null || version1.All(aContent.Contains) ? choice : otherwise.Value,
				CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
		return result;
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
