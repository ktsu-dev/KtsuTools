// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Markdown;

using ktsu.Frontmatter;
using ktsu.Semantics.Paths;
using Spectre.Console;

/// <summary>
/// Service for cleaning and linting markdown files.
/// </summary>
public class MarkdownService
{
	private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
	{
		".git",
		"node_modules",
		"bin",
		"obj",
	};

	/// <summary>
	/// Recursively finds and cleans all markdown files in the specified directory.
	/// </summary>
	/// <param name="directoryPath">The root directory to scan for markdown files.</param>
	/// <param name="applyLinting">Whether to apply markdown linting rules.</param>
	/// <param name="standardizeLineEndings">Whether to standardize line endings.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The number of files that were modified.</returns>
#pragma warning disable CA1822, S2325 // Mark members as static - instance method required for DI injection
	public async Task<int> CleanAsync(AbsoluteDirectoryPath directoryPath, bool applyLinting, bool standardizeLineEndings, CancellationToken ct)
#pragma warning restore CA1822, S2325
	{
		Ensure.NotNull(directoryPath);
		string fullPath = directoryPath.ToString();

		if (!Directory.Exists(fullPath))
		{
			AnsiConsole.MarkupLine($"[red]Error: Directory '{fullPath.EscapeMarkup()}' does not exist.[/]");
			return 0;
		}

		List<string> markdownFiles = FindMarkdownFiles(fullPath);

		if (markdownFiles.Count == 0)
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
				ProgressTask task = progressContext.AddTask("[green]Cleaning markdown files[/]", maxValue: markdownFiles.Count);

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

		AnsiConsole.MarkupLine($"[green]Cleaned {modifiedCount} of {markdownFiles.Count} files.[/]");
		return modifiedCount;
	}

	/// <summary>
	/// Recursively finds and lints all markdown files in the specified directory.
	/// </summary>
	/// <param name="directoryPath">The root directory to scan for markdown files.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The number of files that were modified.</returns>
#pragma warning disable CA1822, S2325 // Mark members as static - instance method required for DI injection
	public async Task<int> LintAsync(AbsoluteDirectoryPath directoryPath, CancellationToken ct)
#pragma warning restore CA1822, S2325
	{
		Ensure.NotNull(directoryPath);
		string fullPath = directoryPath.ToString();

		if (!Directory.Exists(fullPath))
		{
			AnsiConsole.MarkupLine($"[red]Error: Directory '{fullPath.EscapeMarkup()}' does not exist.[/]");
			return 0;
		}

		List<string> markdownFiles = FindMarkdownFiles(fullPath);

		if (markdownFiles.Count == 0)
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
				ProgressTask task = progressContext.AddTask("[green]Linting markdown files[/]", maxValue: markdownFiles.Count);

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

		AnsiConsole.MarkupLine($"[green]Linted {modifiedCount} of {markdownFiles.Count} files.[/]");
		return modifiedCount;
	}

	/// <summary>
	/// Finds the markdown files below <paramref name="root"/> that belong to it. Skips <c>.git</c>,
	/// <c>node_modules</c>, <c>bin</c> and <c>obj</c>, and any subdirectory holding a <c>.git</c>
	/// entry, which is a nested repository or a submodule. Directories that can't be read are skipped.
	/// </summary>
	/// <param name="root">The directory to search.</param>
	/// <returns>The full paths of the markdown files, in ordinal order.</returns>
	internal static List<string> FindMarkdownFiles(string root)
	{
		EnumerationOptions options = new()
		{
			IgnoreInaccessible = true,
			RecurseSubdirectories = false,
			AttributesToSkip = 0,
		};

		List<string> files = [];
		Stack<string> pending = new();
		pending.Push(root);

		while (pending.Count > 0)
		{
			string directory = pending.Pop();
			files.AddRange(Directory.EnumerateFiles(directory, "*.md", options));

			foreach (string subdirectory in Directory.EnumerateDirectories(directory, "*", options))
			{
				if (!IsSkippedDirectory(subdirectory))
				{
					pending.Push(subdirectory);
				}
			}
		}

		files.Sort(StringComparer.Ordinal);
		return files;
	}

	private static bool IsSkippedDirectory(string directory)
	{
		string name = Path.GetFileName(directory);
		return SkippedDirectoryNames.Contains(name)
			|| Directory.Exists(Path.Combine(directory, ".git"))
			|| File.Exists(Path.Combine(directory, ".git"));
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
