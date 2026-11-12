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
		return await ProcessMarkdownFilesAsync(
			directoryPath.ToString(),
			"Cleaning",
			"Updated",
			"Cleaned",
			(filePath, token) => ProcessFileForCleanAsync(filePath, applyLinting, standardizeLineEndings, token),
			ct).ConfigureAwait(false);
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
		return await ProcessMarkdownFilesAsync(directoryPath.ToString(), "Linting", "Linted", "Linted", LintFileAsync, ct).ConfigureAwait(false);
	}

	/// <summary>
	/// Runs <paramref name="processFile"/> over every markdown file <see cref="FindMarkdownFiles"/> finds
	/// below <paramref name="fullPath"/>, with a progress bar, and reports each file it changed.
	/// </summary>
	/// <param name="fullPath">The root directory to scan.</param>
	/// <param name="taskVerb">The progress bar's verb, such as "Linting".</param>
	/// <param name="changedLabel">The label for each changed file, such as "Linted".</param>
	/// <param name="summaryVerb">The summary's verb, such as "Linted".</param>
	/// <param name="processFile">Processes one file and returns whether it changed.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The number of files that were modified.</returns>
	private static async Task<int> ProcessMarkdownFilesAsync(
		string fullPath,
		string taskVerb,
		string changedLabel,
		string summaryVerb,
		Func<string, CancellationToken, Task<bool>> processFile,
		CancellationToken ct)
	{
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
				ProgressTask task = progressContext.AddTask($"[green]{taskVerb} markdown files[/]", maxValue: markdownFiles.Count);

				foreach (string filePath in markdownFiles)
				{
					ct.ThrowIfCancellationRequested();

					bool changed = await processFile(filePath, ct).ConfigureAwait(false);

					if (changed)
					{
						modifiedCount++;
						string relativePath = Path.GetRelativePath(fullPath, filePath);
						AnsiConsole.MarkupLine($"  [blue]{changedLabel}:[/] {relativePath.EscapeMarkup()}");
					}

					task.Increment(1);
				}
			}).ConfigureAwait(false);

		AnsiConsole.MarkupLine($"[green]{summaryVerb} {modifiedCount} of {markdownFiles.Count} files.[/]");
		return modifiedCount;
	}

	/// <summary>
	/// Finds the markdown files below <paramref name="root"/> that belong to it. Skips <c>.git</c>,
	/// <c>node_modules</c>, <c>bin</c> and <c>obj</c>, and any subdirectory holding a <c>.git</c>
	/// entry, which is a nested repository or a submodule. Directories that can't be read are skipped,
	/// and so are linked directories (symlinks and junctions): a link back up the tree would list every
	/// file again at each level, and one pointing elsewhere would reach files outside the root.
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
			|| IsLinkedDirectory(directory)
			|| Directory.Exists(Path.Combine(directory, ".git"))
			|| File.Exists(Path.Combine(directory, ".git"));
	}

	private static bool IsLinkedDirectory(string directory)
	{
		DirectoryInfo info = new(directory);
		return info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint);
	}

	/// <summary>
	/// Processes a single markdown file for cleaning.
	/// </summary>
	private static async Task<bool> ProcessFileForCleanAsync(string filePath, bool applyLinting, bool standardizeLineEndings, CancellationToken ct)
	{
		string originalContent = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
		string workingContent = originalContent;

		// Only trim the end: leading white space can be content, such as an indented code block.
		// Without --normalize-line-endings, whatever this step writes keeps the file's own endings.
		string lineEnding = standardizeLineEndings ? Environment.NewLine : DetectLineEnding(originalContent);

		if (standardizeLineEndings)
		{
			workingContent = workingContent.ReplaceLineEndings().TrimEnd() + Environment.NewLine;
		}

		string combined = Frontmatter.CombineFrontmatter(
			workingContent,
			FrontmatterNaming.Standard,
			FrontmatterOrder.Sorted,
			FrontmatterMergeStrategy.Conservative);

		// A file the front-matter step leaves alone is left byte-for-byte as it was.
		if (combined != workingContent)
		{
			workingContent = combined.ReplaceLineEndings(lineEnding).TrimEnd() + lineEnding;
		}

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
	/// Returns the line ending a file was written with: CRLF when it has any, otherwise LF.
	/// </summary>
	private static string DetectLineEnding(string content) =>
		content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

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
