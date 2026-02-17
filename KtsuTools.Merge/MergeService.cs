// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Merge;

using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using KtsuTools.Core.Services.Settings;
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

	/// <summary>Skip this block.</summary>
	Skip,
}

/// <summary>
/// Service for N-way iterative file merging with interactive conflict resolution.
/// </summary>
public class MergeService(ISettingsService settingsService)
{
	private readonly ISettingsService settingsService = settingsService;

	/// <summary>
	/// Runs the merge operation for files matching a pattern in a directory.
	/// </summary>
	public async Task<int> RunMergeAsync(string directory, string filename, string? batchName = null, CancellationToken ct = default)
	{
		_ = settingsService;
		_ = batchName;
		Ensure.NotNull(directory);
		Ensure.NotNull(filename);

		string fullPath = Path.GetFullPath(directory);

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

			// Read both files
			string content1 = await File.ReadAllTextAsync(bestPair.FilePath1, ct).ConfigureAwait(false);
			string content2 = await File.ReadAllTextAsync(bestPair.FilePath2, ct).ConfigureAwait(false);

			// Show diff
			ShowDiff(content1, content2, bestPair.FilePath1, bestPair.FilePath2);

			// Interactive merge
			string mergedContent = InteractiveMerge(content1, content2);

			// Update all files in both groups
			FileGroup? group1 = versions.Find(g => g.FilePaths.Contains(bestPair.FilePath1));
			FileGroup? group2 = versions.Find(g => g.FilePaths.Contains(bestPair.FilePath2));

			if (group1 is not null && group2 is not null)
			{
				Collection<string> allPaths = [.. group1.FilePaths, .. group2.FilePaths];

				foreach (string filePath in allPaths)
				{
					await File.WriteAllTextAsync(filePath, mergedContent, ct).ConfigureAwait(false);
					string relativePath = Path.GetRelativePath(fullPath, filePath);
					AnsiConsole.MarkupLine($"  [green]Updated:[/] {relativePath.EscapeMarkup()}");
				}

				versions.Remove(group1);
				versions.Remove(group2);

				string mergedHash = ComputeHash(mergedContent);
				versions.Add(new FileGroup(mergedHash, allPaths));
			}
		}

		AnsiConsole.MarkupLine("[bold green]Merge complete. All files now have identical content.[/]");
		return 0;
	}

	private static List<string> FindFiles(string directory, string pattern)
	{
		try
		{
			return [.. Directory.GetFiles(directory, pattern, SearchOption.AllDirectories)];
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
			string content = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
			string hash = ComputeHash(content);

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

	private static string ComputeHash(string content)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(content);
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
					File.ReadAllText(file1),
					File.ReadAllText(file2));

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

	private static void ShowDiff(string content1, string content2, string path1, string path2)
	{
		SideBySideDiffBuilder diffBuilder = new(new Differ());
		SideBySideDiffModel diff = diffBuilder.BuildDiffModel(content1, content2);

		AnsiConsole.MarkupLine($"[dim]--- {Path.GetFileName(path1).EscapeMarkup()}[/]");
		AnsiConsole.MarkupLine($"[dim]+++ {Path.GetFileName(path2).EscapeMarkup()}[/]");

		int maxLines = Math.Min(50, Math.Max(diff.OldText.Lines.Count, diff.NewText.Lines.Count));

		for (int i = 0; i < maxLines; i++)
		{
			DiffPiece? oldLine = i < diff.OldText.Lines.Count ? diff.OldText.Lines[i] : null;
			DiffPiece? newLine = i < diff.NewText.Lines.Count ? diff.NewText.Lines[i] : null;

			if (oldLine?.Type == ChangeType.Deleted)
			{
				AnsiConsole.MarkupLine($"[red]- {oldLine.Text?.EscapeMarkup() ?? string.Empty}[/]");
			}
			else if (newLine?.Type == ChangeType.Inserted)
			{
				AnsiConsole.MarkupLine($"[green]+ {newLine.Text?.EscapeMarkup() ?? string.Empty}[/]");
			}
			else if (oldLine?.Type == ChangeType.Modified)
			{
				AnsiConsole.MarkupLine($"[red]- {oldLine.Text?.EscapeMarkup() ?? string.Empty}[/]");
				if (newLine is not null)
				{
					AnsiConsole.MarkupLine($"[green]+ {newLine.Text?.EscapeMarkup() ?? string.Empty}[/]");
				}
			}
		}

		if (Math.Max(diff.OldText.Lines.Count, diff.NewText.Lines.Count) > maxLines)
		{
			AnsiConsole.MarkupLine("[dim]... (truncated)[/]");
		}

		AnsiConsole.WriteLine();
	}

	private static string InteractiveMerge(string content1, string content2)
	{
		Differ differ = new();
		DiffPlex.Model.DiffResult diffResult = differ.CreateLineDiffs(content1, content2, false);

		if (diffResult.DiffBlocks.Count == 0)
		{
			return content1;
		}

		string[] lines1 = content1.Split('\n');
		string[] lines2 = content2.Split('\n');
		List<string> mergedLines = [];
		int currentLine1 = 0;

		foreach (DiffPlex.Model.DiffBlock block in diffResult.DiffBlocks)
		{
			// Add unchanged lines before this block
			AddUnchangedLines(mergedLines, lines1, currentLine1, block.DeleteStartA);

			// Resolve the conflicting block
			string[] deletedLines = [.. lines1.Skip(block.DeleteStartA).Take(block.DeleteCountA)];
			string[] insertedLines = [.. lines2.Skip(block.InsertStartB).Take(block.InsertCountB)];

			ResolveConflictBlock(mergedLines, deletedLines, insertedLines);
			currentLine1 = block.DeleteStartA + block.DeleteCountA;
		}

		// Add remaining unchanged lines
		AddUnchangedLines(mergedLines, lines1, currentLine1, lines1.Length);

		return string.Join('\n', mergedLines);
	}

	private static void AddUnchangedLines(List<string> mergedLines, string[] lines, int from, int to)
	{
		for (int i = from; i < to && i < lines.Length; i++)
		{
			mergedLines.Add(lines[i]);
		}
	}

	private static void ResolveConflictBlock(List<string> mergedLines, string[] deletedLines, string[] insertedLines)
	{
		AnsiConsole.Write(new Rule("[yellow]Conflict[/]").LeftJustified());
		DisplayConflictSide(deletedLines, "red", "Version 1:");
		DisplayConflictSide(insertedLines, "green", "Version 2:");

		BlockChoice choice = AnsiConsole.Prompt(
			new SelectionPrompt<BlockChoice>()
				.Title("Choose resolution:")
				.AddChoices(BlockChoice.UseVersion1, BlockChoice.UseVersion2, BlockChoice.UseBoth, BlockChoice.Skip));

		switch (choice)
		{
			case BlockChoice.UseVersion1:
				mergedLines.AddRange(deletedLines);
				break;
			case BlockChoice.UseVersion2:
				mergedLines.AddRange(insertedLines);
				break;
			case BlockChoice.UseBoth:
				mergedLines.AddRange(deletedLines);
				mergedLines.AddRange(insertedLines);
				break;
			case BlockChoice.Skip:
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
