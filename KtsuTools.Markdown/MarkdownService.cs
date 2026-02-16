// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Markdown;

using Spectre.Console;

using ktsu.Frontmatter;

/// <summary>
/// Service for cleaning and linting markdown files.
/// </summary>
public class MarkdownService
{
	/// <summary>
	/// Recursively finds and cleans all markdown files in the specified directory.
	/// </summary>
	/// <param name="directoryPath">The root directory to scan for markdown files.</param>
	/// <param name="applyLinting">Whether to apply markdown linting rules.</param>
	/// <param name="standardizeLineEndings">Whether to standardize line endings.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The number of files that were modified.</returns>
#pragma warning disable CA1822 // Mark members as static - instance method required for DI injection
	public async Task<int> CleanAsync(string directoryPath, bool applyLinting, bool standardizeLineEndings, CancellationToken ct)
#pragma warning restore CA1822
	{
		string fullPath = Path.GetFullPath(directoryPath);

		if (!Directory.Exists(fullPath))
		{
			AnsiConsole.MarkupLine($"[red]Error: Directory '{fullPath.EscapeMarkup()}' does not exist.[/]");
			return 0;
		}

		string[] markdownFiles = Directory.GetFiles(fullPath, "*.md", SearchOption.AllDirectories);

		if (markdownFiles.Length == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No markdown files found.[/]");
			return 0;
		}

		int modifiedCount = 0;

		await AnsiConsole.Progress()
			.AutoClear(false)
			.HideCompleted(false)
			.StartAsync(async progressContext =>
			{
				ProgressTask task = progressContext.AddTask("[green]Cleaning markdown files[/]", maxValue: markdownFiles.Length);

				foreach (string filePath in markdownFiles)
				{
					ct.ThrowIfCancellationRequested();

					bool changed = await ProcessFileForCleanAsync(filePath, applyLinting, standardizeLineEndings, ct).ConfigureAwait(false);

					if (changed)
					{
						modifiedCount++;
						string relativePath = Path.GetRelativePath(fullPath, filePath);
						AnsiConsole.MarkupLine($"  [blue]Updated:[/] {relativePath.EscapeMarkup()}");
					}

					task.Increment(1);
				}
			}).ConfigureAwait(false);

		AnsiConsole.MarkupLine($"[green]Cleaned {modifiedCount} of {markdownFiles.Length} files.[/]");
		return modifiedCount;
	}

	/// <summary>
	/// Recursively finds and lints all markdown files in the specified directory.
	/// </summary>
	/// <param name="directoryPath">The root directory to scan for markdown files.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The number of files that were modified.</returns>
#pragma warning disable CA1822 // Mark members as static - instance method required for DI injection
	public async Task<int> LintAsync(string directoryPath, CancellationToken ct)
#pragma warning restore CA1822
	{
		string fullPath = Path.GetFullPath(directoryPath);

		if (!Directory.Exists(fullPath))
		{
			AnsiConsole.MarkupLine($"[red]Error: Directory '{fullPath.EscapeMarkup()}' does not exist.[/]");
			return 0;
		}

		string[] markdownFiles = Directory.GetFiles(fullPath, "*.md", SearchOption.AllDirectories);

		if (markdownFiles.Length == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No markdown files found.[/]");
			return 0;
		}

		int modifiedCount = 0;

		await AnsiConsole.Progress()
			.AutoClear(false)
			.HideCompleted(false)
			.StartAsync(async progressContext =>
			{
				ProgressTask task = progressContext.AddTask("[green]Linting markdown files[/]", maxValue: markdownFiles.Length);

				foreach (string filePath in markdownFiles)
				{
					ct.ThrowIfCancellationRequested();

					bool changed = await LintFileAsync(filePath, ct).ConfigureAwait(false);

					if (changed)
					{
						modifiedCount++;
						string relativePath = Path.GetRelativePath(fullPath, filePath);
						AnsiConsole.MarkupLine($"  [blue]Linted:[/] {relativePath.EscapeMarkup()}");
					}

					task.Increment(1);
				}
			}).ConfigureAwait(false);

		AnsiConsole.MarkupLine($"[green]Linted {modifiedCount} of {markdownFiles.Length} files.[/]");
		return modifiedCount;
	}

	/// <summary>
	/// Processes a single markdown file for cleaning.
	/// </summary>
	private static async Task<bool> ProcessFileForCleanAsync(string filePath, bool applyLinting, bool standardizeLineEndings, CancellationToken ct)
	{
		string originalContent = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
		string workingContent = originalContent;

		if (standardizeLineEndings)
		{
			workingContent = workingContent.ReplaceLineEndings().Trim() + Environment.NewLine;
		}

		workingContent = Frontmatter.CombineFrontmatter(
			workingContent,
			FrontmatterNaming.Standard,
			FrontmatterOrder.Sorted,
			FrontmatterMergeStrategy.Conservative).Trim() + Environment.NewLine;

		if (applyLinting)
		{
			string? configPath = MarkdownLint.FindNearestConfigPath(filePath);
			workingContent = MarkdownLint.FormatMarkdown(workingContent, configPath);
		}

		if (originalContent != workingContent)
		{
			await File.WriteAllTextAsync(filePath, workingContent, ct).ConfigureAwait(false);
			return true;
		}

		return false;
	}

	/// <summary>
	/// Lints a single markdown file.
	/// </summary>
	private static async Task<bool> LintFileAsync(string filePath, CancellationToken ct)
	{
		string originalContent = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);

		string? configPath = MarkdownLint.FindNearestConfigPath(filePath);
		string lintedContent = MarkdownLint.FormatMarkdown(originalContent, configPath);

		if (originalContent != lintedContent)
		{
			await File.WriteAllTextAsync(filePath, lintedContent, ct).ConfigureAwait(false);
			return true;
		}

		return false;
	}
}
