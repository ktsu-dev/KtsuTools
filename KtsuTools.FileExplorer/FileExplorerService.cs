// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.FileExplorer;

using ktsu.Semantics.Paths;
using Spectre.Console;

/// <summary>
/// Model representing a file system item for display.
/// </summary>
public record FileSystemEntry
{
	/// <summary>Gets the name of the item.</summary>
	public required string Name { get; init; }

	/// <summary>Gets the full path of the item.</summary>
	public required string FullPath { get; init; }

	/// <summary>Gets a value indicating whether this is a directory.</summary>
	public bool IsDirectory { get; init; }

	/// <summary>Gets the file size in bytes.</summary>
	public long Size { get; init; }

	/// <summary>Gets the last modification time.</summary>
	public DateTime LastWriteTime { get; init; }

	/// <summary>Gets a value indicating whether the item is hidden.</summary>
	public bool IsHidden { get; init; }

	/// <summary>Gets a human-readable file size string.</summary>
	public string FormattedSize => IsDirectory ? "<DIR>" : FormatFileSize(Size);

	private static string FormatFileSize(long bytes) => bytes switch
	{
		< 1024L => $"{bytes} B",
		< 1024L * 1024L => $"{bytes / 1024.0:F1} KB",
		< 1024L * 1024L * 1024L => $"{bytes / (1024.0 * 1024.0):F1} MB",
		_ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB",
	};
}

/// <summary>
/// TUI file explorer service.
/// </summary>
public class FileExplorerService
{
	private readonly List<string> navigationHistory = [];
	private int historyIndex = -1;

	/// <summary>
	/// Runs the interactive file explorer TUI.
	/// </summary>
	/// <param name="startPath">Absolute starting directory.</param>
	/// <param name="showHidden">Whether to show hidden entries.</param>
	/// <param name="showSizes">Whether to show file sizes.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>Exit code (0 for success).</returns>
#pragma warning disable CA1822 // Mark members as static - instance method required for DI injection
	public async Task<int> RunAsync(AbsoluteDirectoryPath startPath, bool showHidden = false, bool showSizes = true, CancellationToken ct = default)
#pragma warning restore CA1822
	{
		string currentPath = startPath.ToString();

		if (!Directory.Exists(currentPath))
		{
			AnsiConsole.MarkupLine($"[red]Error: Directory '{currentPath.EscapeMarkup()}' does not exist.[/]");
			return 1;
		}

		navigationHistory.Add(currentPath);
		historyIndex = 0;

		while (!ct.IsCancellationRequested)
		{
			AnsiConsole.Clear();
			AnsiConsole.Write(new Rule($"[blue]{currentPath.EscapeMarkup()}[/]").LeftJustified());

			List<FileSystemEntry> entries = GetDirectoryContents(currentPath, showHidden);
			RenderDirectoryTable(entries, showSizes);

			string choice = PromptNavigation(entries);

			if (choice == "[Q] Quit")
			{
				break;
			}

			currentPath = HandleNavigationChoice(choice, currentPath);
		}

		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}

	private static void RenderDirectoryTable(List<FileSystemEntry> entries, bool showSizes)
	{
		Table table = new()
		{
			Border = TableBorder.Rounded,
		};
		table.AddColumn("Name");

		if (showSizes)
		{
			table.AddColumn(new TableColumn("Size").RightAligned());
		}

		table.AddColumn("Modified");

		foreach (FileSystemEntry entry in entries)
		{
			string nameDisplay = entry.IsDirectory
				? $"[blue]{entry.Name.EscapeMarkup()}/[/]"
				: entry.Name.EscapeMarkup();

			if (showSizes)
			{
				table.AddRow(nameDisplay, entry.FormattedSize, entry.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
			}
			else
			{
				table.AddRow(nameDisplay, entry.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
			}
		}

		AnsiConsole.Write(table);
		AnsiConsole.MarkupLine($"[dim]{entries.Count(e => e.IsDirectory)} dir(s), {entries.Count(e => !e.IsDirectory)} file(s)[/]");
	}

	private string PromptNavigation(List<FileSystemEntry> entries)
	{
		List<string> choices = [.. entries.Where(e => e.IsDirectory).Select(e => $"[DIR] {e.Name}")];

		choices.Add("[..] Go up");

		if (historyIndex > 0)
		{
			choices.Add("[<] Back");
		}

		choices.Add("[D] Select drive");
		choices.Add("[Q] Quit");

		return AnsiConsole.Prompt(
			new SelectionPrompt<string>()
				.Title("Navigate:")
				.PageSize(20)
				.AddChoices(choices));
	}

	private string HandleNavigationChoice(string choice, string currentPath)
	{
		if (choice == "[..] Go up")
		{
			string? parent = Directory.GetParent(currentPath)?.FullName;
			if (parent is not null)
			{
				PushNavigation(parent);
				return parent;
			}
		}
		else if (choice == "[<] Back" && historyIndex > 0)
		{
			historyIndex--;
			return navigationHistory[historyIndex];
		}
		else if (choice == "[D] Select drive")
		{
			string? drivePath = SelectDrive();
			if (drivePath is not null)
			{
				PushNavigation(drivePath);
				return drivePath;
			}
		}
		else if (choice.StartsWith("[DIR] ", StringComparison.Ordinal))
		{
			string dirName = choice[6..];
			string newPath = Path.Combine(currentPath, dirName);
			if (Directory.Exists(newPath))
			{
				PushNavigation(newPath);
				return newPath;
			}
		}

		return currentPath;
	}

	private void PushNavigation(string path)
	{
		if (historyIndex < navigationHistory.Count - 1)
		{
			navigationHistory.RemoveRange(historyIndex + 1, navigationHistory.Count - historyIndex - 1);
		}

		navigationHistory.Add(path);
		historyIndex = navigationHistory.Count - 1;

		if (navigationHistory.Count > 100)
		{
			navigationHistory.RemoveAt(0);
			historyIndex--;
		}
	}

	private static List<FileSystemEntry> GetDirectoryContents(string path, bool showHidden)
	{
		List<FileSystemEntry> entries = [];

		try
		{
			foreach (string dir in Directory.GetDirectories(path))
			{
				try
				{
					DirectoryInfo info = new(dir);
					bool isHidden = (info.Attributes & FileAttributes.Hidden) != 0;
					if (!showHidden && isHidden)
					{
						continue;
					}

					entries.Add(new FileSystemEntry
					{
						Name = info.Name,
						FullPath = info.FullName,
						IsDirectory = true,
						LastWriteTime = info.LastWriteTime,
						IsHidden = isHidden,
					});
				}
				catch (UnauthorizedAccessException)
				{
					// Skip inaccessible directories
				}
			}

			foreach (string file in Directory.GetFiles(path))
			{
				try
				{
					FileInfo info = new(file);
					bool isHidden = (info.Attributes & FileAttributes.Hidden) != 0;
					if (!showHidden && isHidden)
					{
						continue;
					}

					entries.Add(new FileSystemEntry
					{
						Name = info.Name,
						FullPath = info.FullName,
						IsDirectory = false,
						Size = info.Length,
						LastWriteTime = info.LastWriteTime,
						IsHidden = isHidden,
					});
				}
				catch (UnauthorizedAccessException)
				{
					// Skip inaccessible files
				}
			}
		}
		catch (UnauthorizedAccessException)
		{
			AnsiConsole.MarkupLine("[red]Access denied.[/]");
		}
		catch (DirectoryNotFoundException)
		{
			AnsiConsole.MarkupLine("[red]Directory not found.[/]");
		}

		return entries;
	}

	private static string? SelectDrive()
	{
		try
		{
			List<string> choices = [];
			DriveInfo[] drives = DriveInfo.GetDrives();

			foreach (DriveInfo drive in drives)
			{
				if (drive.IsReady)
				{
					string label = string.IsNullOrEmpty(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
					choices.Add($"{drive.Name} ({label})");
				}
			}

			if (choices.Count == 0)
			{
				AnsiConsole.MarkupLine("[yellow]No drives available.[/]");
				return null;
			}

			choices.Add("[Cancel]");

			string selection = AnsiConsole.Prompt(
				new SelectionPrompt<string>()
					.Title("Select drive:")
					.AddChoices(choices));

			if (selection == "[Cancel]")
			{
				return null;
			}

			int parenIndex = selection.IndexOf('(', StringComparison.Ordinal);
			return parenIndex > 0 ? selection[..(parenIndex - 1)] : selection;
		}
		catch (IOException)
		{
			AnsiConsole.MarkupLine("[red]Error accessing drives.[/]");
			return null;
		}
	}
}
