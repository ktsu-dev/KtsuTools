// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using KtsuTools.Markdown;

[TestClass]
public class MarkdownLintTests
{
	[TestMethod]
	public void FormatMarkdownEnsuresFinalNewline()
	{
		string result = MarkdownLint.FormatMarkdown("# Title", configPath: null);
		Assert.IsTrue(result.EndsWith(Environment.NewLine, StringComparison.Ordinal));
	}

	[TestMethod]
	public void FormatMarkdownAddsSpaceAfterHeadingMarker()
	{
		string input = "##Heading" + Environment.NewLine;
		string result = MarkdownLint.FormatMarkdown(input, configPath: null);
		StringAssert.Contains(result, "## Heading");
	}

	[TestMethod]
	public void FormatMarkdownCollapsesConsecutiveBlankLines()
	{
		string input = string.Join(Environment.NewLine, ["Line 1", string.Empty, string.Empty, string.Empty, "Line 2"]);
		string result = MarkdownLint.FormatMarkdown(input, configPath: null);
		Assert.IsFalse(result.Contains(Environment.NewLine + Environment.NewLine + Environment.NewLine, StringComparison.Ordinal),
			"Expected MD012 to collapse 3+ blank lines down to the configured maximum.");
	}

	[TestMethod]
	public void FormatMarkdownTrimsTrailingWhitespace()
	{
		string input = "Line with trailing space   " + Environment.NewLine;
		string result = MarkdownLint.FormatMarkdown(input, configPath: null);
		Assert.IsFalse(result.Contains(" " + Environment.NewLine, StringComparison.Ordinal),
			"Expected trailing whitespace to be removed.");
	}
}
