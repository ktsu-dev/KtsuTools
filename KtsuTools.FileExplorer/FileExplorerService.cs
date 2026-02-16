// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.FileExplorer;

using KtsuTools.Core.Services.Settings;
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
public class FileExplorerService(ISettingsService settingsService)
{
	private readonly ISettingsService settingsService = settingsService;
	private readonly List<string> navigationHistory = [];
	private int historyIndex = -1;

	/// <summary>
	/// Runs the interactive file explorer TUI.
	/// </summary>
#pragma warning disable CA1822 // Mark members as static - instance method required for DI injection
	public async Task<int> RunAsync(string startPath = ".", bool showHidden = false, bool showSizes = true, CancellationToken ct = default)
#pragma warning restore CA1822
	{
		_ = settingsService;
		string currentPath = Path.GetFullPath(startPath);

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

			// Display table
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

			// Build navigation choices
			List<string> choices = [];
			foreach (FileSystemEntry entry in entries.Where(e => e.IsDirectory))
			{
				choices.Add($"[DIR] {entry.Name}");
			}

			choices.Add("[..] Go up");

			if (historyIndex > 0)
			{
				choices.Add("[<] Back");
			}

			choices.Add("[D] Select drive");
			choices.Add("[Q] Quit");

			string choice = AnsiConsole.Prompt(
				new SelectionPrompt<string>()
					.Title("Navigate:")
					.PageSize(20)
					.AddChoices(choices));

			if (choice == "[Q] Quit")
			{
				break;
			}

			if (choice == "[..] Go up")
			{
				string? parent = Directory.GetParent(currentPath)?.FullName;
				if (parent is not null)
				{
					currentPath = parent;
					PushNavigation(currentPath);
				}
			}
			else if (choice == "[<] Back")
			{
				if (historyIndex > 0)
				{
					historyIndex--;
					currentPath = navigationHistory[historyIndex];
				}
			}
			else if (choice == "[D] Select drive")
			{
				string? drivePath = SelectDrive();
				if (drivePath is not null)
				{
					currentPath = drivePath;
					PushNavigation(currentPath);
				}
			}
			else if (choice.StartsWith("[DIR] ", StringComparison.Ordinal))
			{
				string dirName = choice[6..];
				string newPath = Path.Combine(currentPath, dirName);
				if (Directory.Exists(newPath))
				{
					currentPath = newPath;
					PushNavigation(currentPath);
				}
			}
		}

		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
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
