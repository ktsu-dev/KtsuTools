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
	public void FormatMarkdownHonoursAJsonConfigWithComments()
	{
		string configPath = WriteConfig(".markdownlint.json", """
			{
				// Keep the asterisk bullets this repository uses
				"MD004": false, // list marker style
				/* block comments are JSONC too */
				"MD012": { "maximum": 1 }
			}
			""");

		string result = MarkdownLint.FormatMarkdown(Lines("* item"), configPath);

		Assert.AreEqual(Lines("* item"), result);
	}

	[TestMethod]
	public void FormatMarkdownHonoursAJsoncConfigWithASchemaUrl()
	{
		string configPath = WriteConfig(".markdownlint.jsonc", """
			{
				"$schema": "https://raw.githubusercontent.com/DavidAnson/markdownlint/main/schema/markdownlint-config-schema.json",
				"MD004": false,
			}
			""");

		string result = MarkdownLint.FormatMarkdown(Lines("* item"), configPath);

		Assert.AreEqual(Lines("* item"), result);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task FormatMarkdownWarnsWhenItCannotParseTheConfig()
	{
		string configPath = WriteConfig(".markdownlint.json", "{ \"MD004\": false");

		string output = await ConsoleCapture.CaptureAsync(() =>
		{
			_ = MarkdownLint.FormatMarkdown(Lines("* item"), configPath);
			return Task.CompletedTask;
		}).ConfigureAwait(false);

		StringAssert.Contains(output, "ignoring markdownlint config");
		StringAssert.Contains(output.ReplaceLineEndings(string.Empty), Path.GetFileName(Path.GetDirectoryName(configPath)!));
	}

	private static string WriteConfig(string fileName, string content)
	{
		string directory = Path.Combine(Path.GetTempPath(), "ktsu-markdownlint-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		string configPath = Path.Combine(directory, fileName);
		File.WriteAllText(configPath, content);
		return configPath;
	}

	private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines) + Environment.NewLine;
}
