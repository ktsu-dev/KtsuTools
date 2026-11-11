// Copyright (c) 2023-2026 ktsu-dev contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ktsu.KtsuTools.Test")]
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

/// <summary>What a navigation choice does.</summary>
internal enum NavigationKind
{
	/// <summary>Enter the named subdirectory.</summary>
	Directory,

	/// <summary>Go to the parent directory.</summary>
	GoUp,

	/// <summary>Go back in the navigation history.</summary>
	Back,

	/// <summary>Pick a drive.</summary>
	SelectDrive,

	/// <summary>Leave the explorer.</summary>
	Quit,
}

/// <summary>
/// One entry in the navigation prompt. The prompt dispatches on <see cref="Kind"/>, so a directory
/// whose name looks like a command (for example <c>[Q] Quit</c>) is still entered, not obeyed.
/// </summary>
/// <param name="Kind">What the choice does.</param>
/// <param name="Name">The directory name, for <see cref="NavigationKind.Directory"/>.</param>
internal sealed record NavigationChoice(NavigationKind Kind, string? Name = null)
{
	/// <summary>Gets the text shown in the prompt, escaped so Spectre does not read it as markup.</summary>
	internal string Display => (Kind switch
	{
		NavigationKind.Directory => $"[DIR] {Name}",
		NavigationKind.GoUp => "[..] Go up",
		NavigationKind.Back => "[<] Back",
		NavigationKind.SelectDrive => "[D] Select drive",
		_ => "[Q] Quit",
	}).EscapeMarkup();
}

/// <summary>One entry in the drive prompt; a null <paramref name="RootPath"/> is Cancel.</summary>
/// <param name="RootPath">The drive's root directory, or null for Cancel.</param>
/// <param name="Label">The drive's volume label.</param>
internal sealed record DriveChoice(string? RootPath, string Label)
{
	/// <summary>Gets the text shown in the prompt, escaped so Spectre does not read it as markup.</summary>
	internal string Display => (RootPath is null ? "[Cancel]" : $"{RootPath} ({Label})").EscapeMarkup();
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
		Ensure.NotNull(startPath);

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

			NavigationChoice choice = PromptNavigation(AnsiConsole.Console, entries);

			if (choice.Kind == NavigationKind.Quit)
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

	internal NavigationChoice PromptNavigation(IAnsiConsole console, List<FileSystemEntry> entries)
	{
		List<NavigationChoice> choices = [.. entries.Where(e => e.IsDirectory).Select(e => new NavigationChoice(NavigationKind.Directory, e.Name))];

		choices.Add(new(NavigationKind.GoUp));

		if (historyIndex > 0)
		{
			choices.Add(new(NavigationKind.Back));
		}

		choices.Add(new(NavigationKind.SelectDrive));
		choices.Add(new(NavigationKind.Quit));

		return console.Prompt(
			new SelectionPrompt<NavigationChoice>()
				.Title("Navigate:")
				.PageSize(20)
				.UseConverter(c => c.Display)
				.AddChoices(choices));
	}

	private string HandleNavigationChoice(NavigationChoice choice, string currentPath)
	{
		if (choice.Kind == NavigationKind.GoUp)
		{
			string? parent = Directory.GetParent(currentPath)?.FullName;
			if (parent is not null)
			{
				PushNavigation(parent);
				return parent;
			}
		}
		else if (choice.Kind == NavigationKind.Back && historyIndex > 0)
		{
			historyIndex--;
			return navigationHistory[historyIndex];
		}
		else if (choice.Kind == NavigationKind.SelectDrive)
		{
			string? drivePath = SelectDrive();
			if (drivePath is not null)
			{
				PushNavigation(drivePath);
				return drivePath;
			}
		}
		else if (choice is { Kind: NavigationKind.Directory, Name: string dirName })
		{
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
			List<DriveChoice> drives = [];

			foreach (DriveInfo drive in DriveInfo.GetDrives())
			{
				if (drive.IsReady)
				{
					string label = string.IsNullOrEmpty(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
					drives.Add(new DriveChoice(drive.Name, label));
				}
			}

			if (drives.Count == 0)
			{
				AnsiConsole.MarkupLine("[yellow]No drives available.[/]");
				return null;
			}

			return PromptDrive(AnsiConsole.Console, drives);
		}
		catch (IOException)
		{
			AnsiConsole.MarkupLine("[red]Error accessing drives.[/]");
			return null;
		}
	}

	internal static string? PromptDrive(IAnsiConsole console, List<DriveChoice> drives)
	{
		List<DriveChoice> choices = [.. drives, new DriveChoice(null, string.Empty)];

		return console.Prompt(
			new SelectionPrompt<DriveChoice>()
				.Title("Select drive:")
				.UseConverter(c => c.Display)
				.AddChoices(choices)).RootPath;
	}
}
