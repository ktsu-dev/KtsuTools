// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Merge;

using System.Collections.ObjectModel;
using System.Security.Cryptography;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using ktsu.Semantics.Paths;
using Spectre.Console;

/// <summary>
/// Represents a group of files with identical content.
/// </summary>
public record FileGroup(string Hash, Collection<string> FilePaths);

/// <summary>
/// Represents similarity between two files.
/// </summary>
public record FileSimilarity(string FilePath1, string FilePath2, double SimilarityScore);

/// <summary>
/// Choice for resolving a merge block.
/// </summary>
public enum BlockChoice
{
	/// <summary>Use version 1.</summary>
	UseVersion1,

	/// <summary>Use version 2.</summary>
	UseVersion2,

	/// <summary>Use both versions.</summary>
	UseBoth,

	/// <summary>Skip this block, leaving it as version 1 has it.</summary>
	Skip,
}

/// <summary>
/// Diff rendering style for conflict display.
/// </summary>
public enum DiffStyle
{
	/// <summary>DiffPlex side-by-side renderer (default; preserves prior behaviour).</summary>
	SideBySide,

	/// <summary>Git-style unified diff via DiffPlex UnifiedDiffBuilder.</summary>
	Git,
}

/// <summary>
/// Service for N-way iterative file merging with interactive conflict resolution.
/// </summary>
public class MergeService
{
	/// <summary>
	/// Runs the merge operation for files matching a pattern in a directory.
	/// </summary>
	/// <param name="directory">Absolute root directory under which to search.</param>
	/// <param name="filename">
	/// Glob pattern to match against filenames. Only copies of the same file name are merged with one
	/// another, so a pattern such as <c>*.yml</c> merges each name's copies on their own.
	/// </param>
	/// <param name="diffStyle">How to render conflict diffs.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>Exit code (0 for success).</returns>
#pragma warning disable CA1822 // Mark members as static - instance method required for DI injection
	public async Task<int> RunMergeAsync(AbsoluteDirectoryPath directory, string filename, DiffStyle diffStyle = DiffStyle.SideBySide, CancellationToken ct = default)
#pragma warning restore CA1822
		=> await RunMergeAsync(directory, filename, diffStyle, chooseBlock: null, ct).ConfigureAwait(false);

	/// <summary>
	/// Runs the merge operation, resolving each conflicting block with <paramref name="chooseBlock"/>.
	/// </summary>
	/// <param name="directory">Absolute root directory under which to search.</param>
	/// <param name="filename">Glob pattern to match against filenames.</param>
	/// <param name="diffStyle">How to render conflict diffs.</param>
	/// <param name="chooseBlock">Picks the resolution for a block. <see langword="null"/> asks the user.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>Exit code (0 for success).</returns>
	internal static async Task<int> RunMergeAsync(AbsoluteDirectoryPath directory, string filename, DiffStyle diffStyle, Func<string[], string[], BlockChoice>? chooseBlock, CancellationToken ct)
	{
		Ensure.NotNull(directory);
		Ensure.NotNull(filename);

		string fullPath = directory.ToString();

		if (!Directory.Exists(fullPath))
		{
			AnsiConsole.MarkupLine($"[red]Error: Directory '{fullPath.EscapeMarkup()}' does not exist.[/]");
			return 1;
		}

		AnsiConsole.MarkupLine($"[bold]Merge[/] - searching for [blue]{filename.EscapeMarkup()}[/] in [blue]{fullPath.EscapeMarkup()}[/]");

		// Find matching files
		List<string> files = FindFiles(fullPath, filename);

		if (files.Count < 2)
		{
			AnsiConsole.MarkupLine($"[yellow]Found {files.Count} file(s). Need at least 2 to merge.[/]");
			return 0;
		}

		AnsiConsole.MarkupLine($"[green]Found {files.Count} file(s) matching pattern.[/]");

		// Only copies of the same file are merged. A pattern such as *.yml also matches unrelated files
		// that merely share an extension, such as a CI workflow and a dependabot config, and merging
		// those would overwrite each with the other's content.
		List<List<string>> nameGroups = [.. files.GroupBy(f => Path.GetFileName(f), FileNameComparer).Select(g => g.ToList())];

		foreach (List<string> copies in nameGroups)
		{
			string name = Path.GetFileName(copies[0]);

			if (nameGroups.Count > 1)
			{
				AnsiConsole.Write(new Rule($"[bold]{name.EscapeMarkup()}[/]").LeftJustified());
			}

			if (copies.Count < 2)
			{
				AnsiConsole.MarkupLine($"[dim]Only one {name.EscapeMarkup()} found. Nothing to merge.[/]");
				continue;
			}

			int exitCode = await MergeCopiesAsync(fullPath, copies, diffStyle, chooseBlock, ct).ConfigureAwait(false);
			if (exitCode != 0)
			{
				return exitCode;
			}
		}

		return 0;
	}

	/// <summary>
	/// File names compare by the platform's rules: case-insensitively on Windows and macOS, where
	/// <c>CI.yml</c> and <c>ci.yml</c> name the same file, and case-sensitively elsewhere.
	/// </summary>
	private static readonly StringComparer FileNameComparer =
		OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

	/// <summary>
	/// Iteratively merges copies of one file until every copy has identical content.
	/// </summary>
	/// <returns>Exit code (0 for success).</returns>
	private static async Task<int> MergeCopiesAsync(string fullPath, List<string> files, DiffStyle diffStyle, Func<string[], string[], BlockChoice>? chooseBlock, CancellationToken ct)
	{
		// Group files by content hash
		Dictionary<string, FileGroup> groups = await GroupFilesByHashAsync(files, ct).ConfigureAwait(false);

		if (groups.Count <= 1)
		{
			AnsiConsole.MarkupLine("[green]All files have identical content. Nothing to merge.[/]");
			return 0;
		}

		AnsiConsole.MarkupLine($"[blue]{groups.Count} unique version(s) found.[/]");

		// Iterative merge
		List<FileGroup> versions = [.. groups.Values];
		int mergeRound = 0;

		while (versions.Count > 1)
		{
			ct.ThrowIfCancellationRequested();
			mergeRound++;

			AnsiConsole.Write(new Rule($"[yellow]Merge Round {mergeRound}[/]").LeftJustified());
			AnsiConsole.MarkupLine($"[blue]{versions.Count} version(s) remaining.[/]");

			// Find the most similar pair
			FileSimilarity bestPair = FindMostSimilarPair(versions);
			AnsiConsole.MarkupLine($"[blue]Merging most similar pair (similarity: {bestPair.SimilarityScore:P1})[/]");

			// Read both files, keeping their encodings so nothing is lost on write-back
			string content1 = (await EncodedText.ReadAsync(bestPair.FilePath1, ct).ConfigureAwait(false)).Text;
			string content2 = (await EncodedText.ReadAsync(bestPair.FilePath2, ct).ConfigureAwait(false)).Text;

			// Show diff
			ShowDiff(content1, content2, bestPair.FilePath1, bestPair.FilePath2, diffStyle);

			// Interactive merge
			string mergedContent = InteractiveMerge(content1, content2, chooseBlock);

			// Update all files in both groups
			FileGroup? group1 = versions.Find(g => g.FilePaths.Contains(bestPair.FilePath1));
			FileGroup? group2 = versions.Find(g => g.FilePaths.Contains(bestPair.FilePath2));

			if (group1 is not null && group2 is not null)
			{
				Collection<string> allPaths = [.. group1.FilePaths, .. group2.FilePaths];

				// Encode the merged text for every file before writing any of them, each in its own
				// encoding and with its own byte order mark, so a file that can't hold the result
				// stops the merge instead of being half-written or silently corrupted.
				List<(string Path, byte[] Bytes)> writes = [];

				foreach (string filePath in allPaths)
				{
					EncodedText stored = await EncodedText.ReadAsync(filePath, ct).ConfigureAwait(false);

					if (!stored.TryEncode(mergedContent, out byte[] bytes))
					{
						string relativePath = Path.GetRelativePath(fullPath, filePath);
						AnsiConsole.MarkupLine($"[red]Error: the merged content has characters that can't be written in the encoding of '{relativePath.EscapeMarkup()}' ({stored.Encoding.WebName.EscapeMarkup()}). No files were changed in this round.[/]");
						return 1;
					}

					writes.Add((filePath, bytes));
				}

				foreach ((string filePath, byte[] bytes) in writes)
				{
					await File.WriteAllBytesAsync(filePath, bytes, ct).ConfigureAwait(false);
					string relativePath = Path.GetRelativePath(fullPath, filePath);
					AnsiConsole.MarkupLine($"  [green]Updated:[/] {relativePath.EscapeMarkup()}");
				}

				versions.Remove(group1);
				versions.Remove(group2);

				string mergedHash = ComputeHash(writes[0].Bytes);
				versions.Add(new FileGroup(mergedHash, allPaths));
			}
		}

		AnsiConsole.MarkupLine("[bold green]Merge complete. All files now have identical content.[/]");
		return 0;
	}

	/// <summary>
	/// Finds the files matching <paramref name="pattern"/> below <paramref name="directory"/>. Linked
	/// directories (symlinks and junctions) are not followed: a link pointing outside the merge directory
	/// would have the merge rewrite files there, and a link back up the tree would list each file again.
	/// </summary>
	internal static List<string> FindFiles(string directory, string pattern)
	{
		try
		{
			List<string> files = [];
			Stack<string> pending = new();
			pending.Push(directory);

			while (pending.Count > 0)
			{
				string current = pending.Pop();
				files.AddRange(Directory.GetFiles(current, pattern));

				foreach (string subdirectory in Directory.GetDirectories(current))
				{
					DirectoryInfo info = new(subdirectory);
					if (info.LinkTarget is null && !info.Attributes.HasFlag(FileAttributes.ReparsePoint))
					{
						pending.Push(subdirectory);
					}
				}
			}

			return files;
		}
		catch (UnauthorizedAccessException)
		{
			AnsiConsole.MarkupLine("[yellow]Warning: Some directories could not be accessed.[/]");
			return [];
		}
	}

	private static async Task<Dictionary<string, FileGroup>> GroupFilesByHashAsync(List<string> files, CancellationToken ct)
	{
		Dictionary<string, FileGroup> groups = [];

		foreach (string file in files)
		{
			ct.ThrowIfCancellationRequested();
			// Hash the raw bytes, so files that differ only by byte order mark or encoding are told apart.
			byte[] bytes = await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
			string hash = ComputeHash(bytes);

			if (EncodedText.Decode(bytes).IsLegacy)
			{
				AnsiConsole.MarkupLine($"[yellow]Warning: '{file.EscapeMarkup()}' is not valid UTF-8. It is read as Latin-1 and written back in Latin-1.[/]");
			}

			if (groups.TryGetValue(hash, out FileGroup? group))
			{
				group.FilePaths.Add(file);
			}
			else
			{
				groups[hash] = new FileGroup(hash, [file]);
			}
		}

		return groups;
	}

	private static string ComputeHash(byte[] bytes)
	{
		byte[] hashBytes = SHA256.HashData(bytes);
		return Convert.ToHexString(hashBytes);
	}

	private static FileSimilarity FindMostSimilarPair(List<FileGroup> versions)
	{
		FileSimilarity? best = null;

		for (int i = 0; i < versions.Count; i++)
		{
			for (int j = i + 1; j < versions.Count; j++)
			{
				string file1 = versions[i].FilePaths[0];
				string file2 = versions[j].FilePaths[0];
				double similarity = CalculateSimilarity(
					EncodedText.Decode(File.ReadAllBytes(file1)).Text,
					EncodedText.Decode(File.ReadAllBytes(file2)).Text);

				if (best is null || similarity > best.SimilarityScore)
				{
					best = new FileSimilarity(file1, file2, similarity);
				}
			}
		}

		return best ?? new FileSimilarity(versions[0].FilePaths[0], versions[1].FilePaths[0], 0.0);
	}

	private static double CalculateSimilarity(string content1, string content2)
	{
		string[] lines1 = content1.Split('\n');
		string[] lines2 = content2.Split('\n');

		Differ differ = new();
		DiffPlex.Model.DiffResult diffResult = differ.CreateLineDiffs(content1, content2, false);

		int totalLines = Math.Max(lines1.Length, lines2.Length);
		if (totalLines == 0)
		{
			return 1.0;
		}

		int unchangedLines = totalLines;
		foreach (DiffPlex.Model.DiffBlock block in diffResult.DiffBlocks)
		{
			unchangedLines -= Math.Max(block.DeleteCountA, block.InsertCountB);
		}

		return Math.Max(0.0, (double)unchangedLines / totalLines);
	}

	/// <summary>Most changed lines a preview shows; unchanged context does not count toward it.</summary>
	internal const int DiffLineCap = 50;

	/// <summary>Unchanged lines the git-style preview shows on each side of a change, as <c>git diff</c> does.</summary>
	internal const int DiffContextLines = 3;

	/// <summary>
	/// Prints a preview of how <paramref name="content2"/> differs from <paramref name="content1"/>.
	/// The cap counts changed lines only, so a difference far down a long file is still shown.
	/// </summary>
	internal static void ShowDiff(string content1, string content2, string path1, string path2, DiffStyle style)
	{
		AnsiConsole.MarkupLine($"[dim]--- {Path.GetFileName(path1).EscapeMarkup()}[/]");
		AnsiConsole.MarkupLine($"[dim]+++ {Path.GetFileName(path2).EscapeMarkup()}[/]");

		switch (style)
		{
			case DiffStyle.Git:
				ShowUnifiedDiff(content1, content2);
				break;
			case DiffStyle.SideBySide:
			default:
				ShowSideBySideDiff(content1, content2);
				break;
		}

		AnsiConsole.WriteLine();
	}

	private static void ShowSideBySideDiff(string content1, string content2)
	{
		SideBySideDiffBuilder diffBuilder = new(new Differ());
		SideBySideDiffModel diff = diffBuilder.BuildDiffModel(content1, content2);

		int total = Math.Max(diff.OldText.Lines.Count, diff.NewText.Lines.Count);
		List<(bool Deleted, string Text)> changed = [];

		for (int i = 0; i < total; i++)
		{
			DiffPiece? oldLine = i < diff.OldText.Lines.Count ? diff.OldText.Lines[i] : null;
			DiffPiece? newLine = i < diff.NewText.Lines.Count ? diff.NewText.Lines[i] : null;

			if (oldLine?.Type is ChangeType.Deleted or ChangeType.Modified)
			{
				changed.Add((true, oldLine.Text ?? string.Empty));
			}

			if (newLine?.Type is ChangeType.Inserted or ChangeType.Modified)
			{
				changed.Add((false, newLine.Text ?? string.Empty));
			}
		}

		foreach ((bool deleted, string text) in changed.Take(DiffLineCap))
		{
			AnsiConsole.MarkupLine(deleted
				? $"[red]- {text.EscapeMarkup()}[/]"
				: $"[green]+ {text.EscapeMarkup()}[/]");
		}

		ShowTruncation(changed.Count - DiffLineCap);
	}

	private static void ShowUnifiedDiff(string content1, string content2)
	{
		List<DiffPiece> lines = [.. InlineDiffBuilder.Diff(content1, content2).Lines.Where(line => line.Type != ChangeType.Imaginary)];
		List<int> changedIndices = [.. Enumerable.Range(0, lines.Count).Where(i => lines[i].Type != ChangeType.Unchanged)];

		// Only changes within the cap are shown; context is taken around those alone, so it
		// never runs on into a change the preview leaves out.
		int shownChanges = Math.Min(DiffLineCap, changedIndices.Count);
		int lastVisible = shownChanges < changedIndices.Count ? changedIndices[shownChanges] - 1 : lines.Count - 1;
		bool[] visible = new bool[lines.Count];

		for (int c = 0; c < shownChanges; c++)
		{
			int index = changedIndices[c];
			int from = Math.Max(0, index - DiffContextLines);
			int to = Math.Min(lastVisible, index + DiffContextLines);

			for (int i = from; i <= to; i++)
			{
				visible[i] = true;
			}
		}

		int previous = -1;

		for (int i = 0; i < lines.Count; i++)
		{
			if (!visible[i])
			{
				continue;
			}

			if (previous >= 0 && i > previous + 1)
			{
				AnsiConsole.MarkupLine("[dim]...[/]");
			}

			previous = i;
			string text = lines[i].Text?.EscapeMarkup() ?? string.Empty;
			AnsiConsole.MarkupLine(lines[i].Type switch
			{
				ChangeType.Inserted => $"[green]+{text}[/]",
				ChangeType.Deleted or ChangeType.Modified => $"[red]-{text}[/]",
				_ => $"[dim] {text}[/]",
			});
		}

		ShowTruncation(changedIndices.Count - shownChanges);
	}

	private static void ShowTruncation(int omittedChangedLines)
	{
		if (omittedChangedLines > 0)
		{
			AnsiConsole.MarkupLine($"[dim]... (truncated: {omittedChangedLines} more changed line{(omittedChangedLines == 1 ? string.Empty : "s")} not shown)[/]");
		}
	}

	/// <summary>
	/// Merges two versions line by line, asking <paramref name="chooseBlock"/> how to resolve each
	/// block where they differ.
	/// </summary>
	/// <param name="content1">Version 1.</param>
	/// <param name="content2">Version 2.</param>
	/// <param name="chooseBlock">
	/// Picks the resolution for a block from its version 1 and version 2 lines. Defaults to asking
	/// the user.
	/// </param>
	/// <returns>The merged content.</returns>
	internal static string InteractiveMerge(string content1, string content2, Func<string[], string[], BlockChoice>? chooseBlock = null)
	{
		chooseBlock ??= PromptForBlockChoice;
		Differ differ = new();
		DiffPlex.Model.DiffResult diffResult = differ.CreateLineDiffs(content1, content2, false);

		if (diffResult.DiffBlocks.Count == 0)
		{
			return content1;
		}

		string[] lines1 = SplitLines(content1);
		string[] lines2 = SplitLines(content2);
		List<string> mergedLines = [];
		int currentLine1 = 0;

		foreach (DiffPlex.Model.DiffBlock block in diffResult.DiffBlocks)
		{
			// Add unchanged lines before this block
			AddUnchangedLines(mergedLines, lines1, currentLine1, block.DeleteStartA);

			// Resolve the conflicting block
			string[] deletedLines = [.. lines1.Skip(block.DeleteStartA).Take(block.DeleteCountA)];
			string[] insertedLines = [.. lines2.Skip(block.InsertStartB).Take(block.InsertCountB)];

			ResolveConflictBlock(mergedLines, deletedLines, insertedLines, chooseBlock);
			currentLine1 = block.DeleteStartA + block.DeleteCountA;
		}

		// Add remaining unchanged lines
		AddUnchangedLines(mergedLines, lines1, currentLine1, lines1.Length);

		return string.Join('\n', mergedLines);
	}

	/// <summary>
	/// Splits content into the lines DiffPlex numbers its blocks by. DiffPlex counts empty content as
	/// no lines at all, where <see cref="string.Split(char, StringSplitOptions)"/> would return one
	/// empty line, which would then be appended after the last block as an extra newline.
	/// </summary>
	private static string[] SplitLines(string content) =>
		content.Length == 0 ? [] : content.Split('\n');

	private static void AddUnchangedLines(List<string> mergedLines, string[] lines, int from, int to)
	{
		for (int i = from; i < to && i < lines.Length; i++)
		{
			mergedLines.Add(lines[i]);
		}
	}

	private static void ResolveConflictBlock(List<string> mergedLines, string[] deletedLines, string[] insertedLines, Func<string[], string[], BlockChoice> chooseBlock)
	{
		AnsiConsole.Write(new Rule("[yellow]Conflict[/]").LeftJustified());
		DisplayConflictSide(deletedLines, "red", "Version 1:");
		DisplayConflictSide(insertedLines, "green", "Version 2:");

		BlockChoice choice = chooseBlock(deletedLines, insertedLines);
		ApplyBlockChoice(mergedLines, deletedLines, insertedLines, choice);
	}

	private static BlockChoice PromptForBlockChoice(string[] deletedLines, string[] insertedLines) =>
		AnsiConsole.Prompt(
			new SelectionPrompt<BlockChoice>()
				.Title("Choose resolution:")
				.AddChoices(BlockChoice.UseVersion1, BlockChoice.UseVersion2, BlockChoice.UseBoth, BlockChoice.Skip));

	/// <summary>
	/// Appends the lines a resolved conflict block contributes to the merged result.
	/// </summary>
	/// <param name="mergedLines">The merged result so far.</param>
	/// <param name="deletedLines">The block as version 1 has it.</param>
	/// <param name="insertedLines">The block as version 2 has it.</param>
	/// <param name="choice">How the block was resolved.</param>
	internal static void ApplyBlockChoice(List<string> mergedLines, string[] deletedLines, string[] insertedLines, BlockChoice choice)
	{
		switch (choice)
		{
			case BlockChoice.UseVersion1:
			case BlockChoice.Skip:
				// Skipping leaves the block as it is in version 1; it must not drop both sides.
				mergedLines.AddRange(deletedLines);
				break;
			case BlockChoice.UseVersion2:
				mergedLines.AddRange(insertedLines);
				break;
			case BlockChoice.UseBoth:
				mergedLines.AddRange(deletedLines);
				mergedLines.AddRange(insertedLines);
				break;
		}
	}

	private static void DisplayConflictSide(string[] lines, string color, string header)
	{
		if (lines.Length == 0)
		{
			return;
		}

		AnsiConsole.MarkupLine($"[{color}]{header}[/]");
		foreach (string line in lines)
		{
			AnsiConsole.MarkupLine($"  [{color}]{line.EscapeMarkup()}[/]");
		}
	}
}
