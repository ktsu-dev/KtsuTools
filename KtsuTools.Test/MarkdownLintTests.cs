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
	[TestMethod]
	[DataRow("```", "```")]
	[DataRow("```csharp", "```")]
	[DataRow("~~~", "~~~")]
	[DataRow("````md", "````")]
	public void FormatMarkdownLeavesFencedCodeUnchanged(string openingFence, string closingFence)
	{
		string input = Lines(
			"# Sample",
			string.Empty,
			openingFence,
			"#!/usr/bin/env python",
			"#include<stdio.h>",
			"#pragma once",
			"#region Setup",
			"def first():",
			"    pass",
			string.Empty,
			string.Empty,
			string.Empty,
			"def second():",
			"    pass",
			"/**",
			" * Doc comment",
			" + not a list",
			" */",
			"-old  ",
			"+new\t",
			"keep three   ",
			closingFence);

		string result = MarkdownLint.FormatMarkdown(input, configPath: null);

		Assert.AreEqual(input, result);
	}

	[TestMethod]
	public void FormatMarkdownKeepsShorterFenceInsideLongerFence()
	{
		string input = Lines(
			"````md",
			"```",
			"#include<stdio.h>",
			"```",
			"#pragma once",
			"````");

		string result = MarkdownLint.FormatMarkdown(input, configPath: null);

		Assert.AreEqual(input, result);
	}

	[TestMethod]
	public void FormatMarkdownResumesFixingAfterAFenceCloses()
	{
		string input = Lines(
			"```",
			"#include<stdio.h>",
			"```",
			"##Heading",
			"* item");

		string result = MarkdownLint.FormatMarkdown(input, configPath: null);

		Assert.AreEqual(Lines("```", "#include<stdio.h>", "```", "## Heading", "- item"), result);
	}

	[TestMethod]
	[DataRow("* * *")]
	[DataRow("- - -")]
	[DataRow("_ _ _")]
	[DataRow("***")]
	public void FormatMarkdownPreservesThematicBreaks(string thematicBreak)
	{
		string input = Lines("Above", string.Empty, thematicBreak, string.Empty, "Below");

		string result = MarkdownLint.FormatMarkdown(input, configPath: null);

		Assert.AreEqual(input, result);
	}

	[TestMethod]
	public void FormatMarkdownKeepsATwoSpaceHardLineBreakWithNoConfig()
	{
		string input = Lines("Line one  ", "Line two");

		string result = MarkdownLint.FormatMarkdown(input, configPath: null);

		Assert.AreEqual(input, result);
	}

	[TestMethod]
	public void FormatMarkdownTrimsTrailingSpacesThatAreNotAHardLineBreak()
	{
		string result = MarkdownLint.FormatMarkdown(Lines("One space ", "Three spaces   ", "Tab\t"), configPath: null);

		Assert.AreEqual(Lines("One space", "Three spaces", "Tab"), result);
	}

	[TestMethod]
	[DataRow("{ \"MD009\": { \"br_spaces\": 3 } }", "Line one   ", "Line one   ")]
	[DataRow("{ \"MD009\": { \"br_spaces\": 3 } }", "Line one  ", "Line one")]
	[DataRow("{ \"MD009\": { \"br_spaces\": 1 } }", "Line one ", "Line one")]
	[DataRow("{ \"MD009\": { \"br_spaces\": 0 } }", "Line one  ", "Line one")]
	[DataRow("{ \"MD009\": { \"strict\": false } }", "Line one  ", "Line one  ")]
	public void FormatMarkdownHonoursAConfiguredHardLineBreakWidth(string config, string line, string expected)
	{
		string configPath = Path.Join(Path.GetTempPath(), $"ktsu_mdlint_{Guid.NewGuid():N}.json");
		File.WriteAllText(configPath, config);
		try
		{
			string result = MarkdownLint.FormatMarkdown(Lines(line, "Line two"), configPath);

			Assert.AreEqual(Lines(expected, "Line two"), result);
		}
		finally
		{
			File.Delete(configPath);
		}
	}

	private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines) + Environment.NewLine;
}
