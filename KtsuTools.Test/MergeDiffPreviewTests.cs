// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using KtsuTools.Merge;

/// <summary>
/// The preview shown before each merge round. The cap must count changed lines, not the
/// unchanged ones before them, so a difference far down a long file is still shown.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MergeDiffPreviewTests
{
	private static string Lines(int count, Func<int, string> line) =>
		string.Join("\n", Enumerable.Range(1, count).Select(line)) + "\n";

	private static async Task<string> PreviewAsync(string content1, string content2, DiffStyle style) =>
		await ConsoleCapture.CaptureAsync(() =>
		{
			MergeService.ShowDiff(content1, content2, "a/.editorconfig", "b/.editorconfig", style);
			return Task.CompletedTask;
		}).ConfigureAwait(false);

	[TestMethod]
	[DataRow(DiffStyle.SideBySide)]
	[DataRow(DiffStyle.Git)]
	public async Task ShowsAChangePastTheCap(DiffStyle style)
	{
		string original = Lines(200, i => $"line {i}");
		string changed = Lines(200, i => i == 120 ? "line 120 changed" : $"line {i}");

		string output = await PreviewAsync(original, changed, style).ConfigureAwait(false);

		StringAssert.Contains(output, "line 120 changed");
		StringAssert.Contains(output, "-" + (style == DiffStyle.Git ? string.Empty : " ") + "line 120");
		Assert.IsFalse(output.Contains("truncated", StringComparison.Ordinal), output);
	}

	[TestMethod]
	public async Task GitStyleShowsOnlyContextAroundTheChange()
	{
		string original = Lines(200, i => $"line {i}");
		string changed = Lines(200, i => i == 120 ? "line 120 changed" : $"line {i}");

		string output = await PreviewAsync(original, changed, DiffStyle.Git).ConfigureAwait(false);

		StringAssert.Contains(output, " line 117");
		StringAssert.Contains(output, " line 123");
		Assert.IsFalse(output.Contains("line 116", StringComparison.Ordinal), output);
		Assert.IsFalse(output.Contains("line 124", StringComparison.Ordinal), output);
		Assert.IsFalse(output.Contains("line 1\n", StringComparison.Ordinal), output);
	}

	[TestMethod]
	[DataRow(DiffStyle.SideBySide)]
	[DataRow(DiffStyle.Git)]
	public async Task TruncatesPastTheCapAndSaysHowManyChangedLinesWereLeftOut(DiffStyle style)
	{
		// Every line differs, so the version 1 and version 2 sides give two changed lines each.
		const int count = 40;
		string original = Lines(count, i => $"old {i}");
		string changed = Lines(count, i => $"new {i}");

		string output = await PreviewAsync(original, changed, style).ConfigureAwait(false);

		int omitted = (count * 2) - MergeService.DiffLineCap;
		StringAssert.Contains(output, $"... (truncated: {omitted} more changed lines not shown)");
	}
}
