// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Sync;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Extensions;
using ktsu.Semantics.Paths;

using KtsuTools.Core.Services.Process;

using LibGit2Sharp;

using Spectre.Console;

/// <summary>
/// Service that synchronizes file contents across multiple repositories.
/// </summary>
public class SyncService(IProcessService processService)
{
	private readonly IProcessService processService = processService;

	private const string CommitAuthorName = "KtsuTools";
	private const string GitDirSuffixWindows = ".git\\";
	private const string GitDirSuffixUnix = ".git/";

	/// <summary>
	/// Runs the sync operation for the specified path and filename.
	/// </summary>
	/// <param name="path">The root path to scan recursively.</param>
	/// <param name="filename">The filename pattern to scan for.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>Exit code (0 for success).</returns>
	public Task<int> RunAsync(AbsoluteDirectoryPath path, string filename, CancellationToken ct = default) =>
		RunAsync(path, [filename], autoPush: false, ct);

	/// <summary>
	/// Runs the sync operation for the specified path and one or more filename patterns.
	/// </summary>
	/// <param name="path">The root path to scan recursively.</param>
	/// <param name="filenames">One or more filename patterns to scan for.</param>
	/// <param name="autoPush">When true, repos whose unpushed commits are all authored by KtsuTools are pushed without prompting.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>Exit code (0 for success).</returns>
	public async Task<int> RunAsync(AbsoluteDirectoryPath path, IReadOnlyList<string> filenames, bool autoPush, CancellationToken ct = default)
	{
		Ensure.NotNull(path);
		ct.ThrowIfCancellationRequested();

		string pathString = path.ToString();

		if (!Directory.Exists(pathString))
		{
			AnsiConsole.MarkupLine($"[red]Path does not exist: {pathString.EscapeMarkup()}[/]");
			return 1;
		}

		List<string> patterns = [.. filenames
			.Select(f => f?.Trim() ?? string.Empty)
			.Where(f => !string.IsNullOrEmpty(f))
			.Distinct(StringComparer.Ordinal)];

		if (patterns.Count == 0)
		{
			AnsiConsole.MarkupLine("[red]No filename patterns provided.[/]");
			return 1;
		}

		AnsiConsole.MarkupLine($"[bold]Scanning for:[/] {string.Join(", ", patterns).EscapeMarkup()}");
		AnsiConsole.MarkupLine($"[bold]In:[/] {pathString.EscapeMarkup()}");
		AnsiConsole.WriteLine();

		HashSet<string> seen = new(StringComparer.Ordinal);
		Collection<string> fileEnumeration = [];
		foreach (string pattern in patterns)
		{
			foreach (string file in Directory.EnumerateFiles(pathString, pattern, SearchOption.AllDirectories)
				.Where(f => !IsRepoNested(AbsoluteFilePath.Create<AbsoluteFilePath>(f).AbsoluteDirectoryPath)))
			{
				if (seen.Add(file))
				{
					fileEnumeration.Add(file);
				}
			}
		}

		IEnumerable<string> uniqueFilenames = fileEnumeration.Select(Path.GetFileName).Distinct()!;
		AnsiConsole.MarkupLine($"[bold]Found matches:[/] {string.Join(", ", uniqueFilenames).EscapeMarkup()}");

		HashSet<string> commitDirectories = [];
		HashSet<string> expandedFilesToSync = [];

		expandedFilesToSync.UnionWith(uniqueFilenames);

		foreach (string uniqueFilename in uniqueFilenames)
		{
			ct.ThrowIfCancellationRequested();
			await ProcessUniqueFilenameAsync(uniqueFilename, fileEnumeration, pathString, commitDirectories, ct).ConfigureAwait(false);
		}

		await CommitChangedFilesAsync(commitDirectories, expandedFilesToSync, pathString).ConfigureAwait(false);

		await PushToRemoteAsync(commitDirectories, pathString, autoPush, ct).ConfigureAwait(false);

		return 0;
	}

	private static async Task ProcessUniqueFilenameAsync(
		string uniqueFilename,
		Collection<string> fileEnumeration,
		string path,
		HashSet<string> commitDirectories,
		CancellationToken ct)
	{
		IEnumerable<string> fileMatches = fileEnumeration.Where(f => Path.GetFileName(f) == uniqueFilename);
		Dictionary<string, Collection<string>> results = [];

		using SHA256 sha256 = SHA256.Create();

		foreach (string file in fileMatches)
		{
			ct.ThrowIfCancellationRequested();
			using FileStream fileStream = new(file, FileMode.Open, FileAccess.Read, FileShare.Read);
			fileStream.Position = 0;
			byte[] hash = await sha256.ComputeHashAsync(fileStream, ct).ConfigureAwait(false);
			string hashStr = HashToString(hash);
			if (!results.TryGetValue(hashStr, out Collection<string>? result))
			{
				result = [];
				results.Add(hashStr, result);
			}

			result.Add(file.Replace(path, "", StringComparison.Ordinal).Replace(uniqueFilename, "", StringComparison.Ordinal).Trim(Path.DirectorySeparatorChar));
		}

		IEnumerable<string> allDirectories = results.SelectMany(r => r.Value);
		commitDirectories.UnionWith(allDirectories);

		if (results.Count > 1)
		{
			await HandleMultipleHashGroupsAsync(results, uniqueFilename, path, ct).ConfigureAwait(false);
		}
		else if (results.Count == 1)
		{
			AnsiConsole.MarkupLine($"[green]All files in sync for:[/] {uniqueFilename.EscapeMarkup()}");
		}
	}

	private static async Task HandleMultipleHashGroupsAsync(
		Dictionary<string, Collection<string>> results,
		string uniqueFilename,
		string path,
		CancellationToken ct)
	{
		Dictionary<string, DateTime> oldestModificationDates = CalculateOldestModificationDates(results, path, uniqueFilename);

		// Sort by oldest modification date (most recent first)
		Dictionary<string, Collection<string>> sortedResults = results
			.OrderByDescending(r => oldestModificationDates[r.Key])
			.ToDictionary(r => r.Key, r => r.Value);

		DisplayHashGroupsTable(sortedResults, uniqueFilename, oldestModificationDates);

		string syncHash = PromptForSyncHash(sortedResults);

		if (!string.IsNullOrWhiteSpace(syncHash))
		{
			await SyncFilesToHashAsync(syncHash, sortedResults, uniqueFilename, path, ct).ConfigureAwait(false);
		}
	}

	private static Dictionary<string, DateTime> CalculateOldestModificationDates(
		Dictionary<string, Collection<string>> results,
		string path,
		string uniqueFilename)
	{
		Dictionary<string, DateTime> oldestModificationDates = [];
		foreach ((string hash, Collection<string> relativeDirectories) in results)
		{
			DateTime oldestModified = relativeDirectories
				.Min(dir => new FileInfo(Path.Combine(path, dir, uniqueFilename)).LastWriteTime);

			oldestModificationDates[hash] = oldestModified;
		}

		return oldestModificationDates;
	}

	private static void DisplayHashGroupsTable(
		Dictionary<string, Collection<string>> results,
		string uniqueFilename,
		Dictionary<string, DateTime> oldestModificationDates)
	{
		AnsiConsole.WriteLine();
		AnsiConsole.MarkupLine($"[bold yellow]Differences found for:[/] {uniqueFilename.EscapeMarkup()}");
		AnsiConsole.WriteLine();

		foreach ((string hash, Collection<string> relativeDirectories) in results)
		{
			Table table = new()
			{
				Title = new TableTitle($"[bold]{hash.EscapeMarkup()}[/] ({oldestModificationDates[hash].ToString("g", CultureInfo.CurrentCulture).EscapeMarkup()})"),
			};
			table.AddColumn("Directory");
			table.Border(TableBorder.Rounded);

			foreach (string dir in relativeDirectories)
			{
				table.AddRow(dir.EscapeMarkup());
			}

			AnsiConsole.Write(table);
			AnsiConsole.WriteLine();
		}
	}

	private static string PromptForSyncHash(Dictionary<string, Collection<string>> results)
	{
		List<string> choices = [.. results.Keys, "(skip)"];

		string selection = AnsiConsole.Prompt(
			new SelectionPrompt<string>()
				.Title("[bold]Select a hash to sync all files to:[/]")
				.PageSize(10)
				.AddChoices(choices));

		return selection == "(skip)" ? string.Empty : selection;
	}

	private static async Task SyncFilesToHashAsync(
		string syncHash,
		Dictionary<string, Collection<string>> results,
		string uniqueFilename,
		string path,
		CancellationToken ct)
	{
		Collection<string> destinationDirectories = results
			.Where(r => r.Key != syncHash)
			.SelectMany(r => r.Value)
			.ToCollection();

		if (!results.TryGetValue(syncHash, out Collection<string>? sourceDirectories))
		{
			AnsiConsole.MarkupLine("[red]Hash not found in results.[/]");
			await Task.CompletedTask.ConfigureAwait(false);
			return;
		}

		string sourceDir = sourceDirectories[0];
		string sourceFile = Path.Combine(path, sourceDir, uniqueFilename);

		AnsiConsole.MarkupLine("[bold]Planned copies:[/]");
		foreach (string dir in destinationDirectories)
		{
			string destinationFile = Path.Combine(path, dir, uniqueFilename);
			AnsiConsole.MarkupLine($"  [blue]{sourceDir.EscapeMarkup()}[/] -> [yellow]{destinationFile.EscapeMarkup()}[/]");
		}

		AnsiConsole.WriteLine();

		bool confirmed = await AnsiConsole.ConfirmAsync("Proceed with sync?", defaultValue: false, ct).ConfigureAwait(false);

		if (confirmed)
		{
			AnsiConsole.WriteLine();
			foreach (string dir in destinationDirectories)
			{
				ct.ThrowIfCancellationRequested();
				string destinationFile = Path.Combine(path, dir, uniqueFilename);
				AnsiConsole.MarkupLine($"[green]Copying:[/] {sourceDir.EscapeMarkup()} -> {destinationFile.EscapeMarkup()}");
				File.Copy(sourceFile, destinationFile, overwrite: true);
			}
		}
	}

	private static bool IsGitRepoPath(string repoPath) =>
		repoPath.EndsWith(GitDirSuffixWindows, StringComparison.Ordinal)
		|| repoPath.EndsWith(GitDirSuffixUnix, StringComparison.Ordinal);

	private static string StripGitSuffix(string repoPath) =>
		repoPath
			.Replace(GitDirSuffixWindows, "", StringComparison.Ordinal)
			.Replace(GitDirSuffixUnix, "", StringComparison.Ordinal);

	private static async Task CommitChangedFilesAsync(
		HashSet<string> commitDirectories,
		HashSet<string> expandedFilesToSync,
		string path)
	{
		AnsiConsole.WriteLine();

		Collection<string> commitFiles = FindChangedFiles(commitDirectories, expandedFilesToSync, path);

		if (commitFiles.Count > 0)
		{
			AnsiConsole.WriteLine();
			bool confirmed = await AnsiConsole.ConfirmAsync("Commit changed files?", defaultValue: false).ConfigureAwait(false);

			if (confirmed)
			{
				AnsiConsole.WriteLine();
				foreach (string filePath in commitFiles)
				{
					CommitFile(filePath);
				}
			}
		}
	}

	private static Collection<string> FindChangedFiles(
		HashSet<string> commitDirectories,
		HashSet<string> expandedFilesToSync,
		string path)
	{
		Collection<string> commitFiles = [];

		foreach (string dir in commitDirectories)
		{
			string directoryPath = Path.Combine(path, dir);
			string repoPath = Repository.Discover(directoryPath);
			if (repoPath is null || !IsGitRepoPath(repoPath))
			{
				continue;
			}

			using Repository repo = new(repoPath);
			foreach (string uniqueFilename in expandedFilesToSync)
			{
				string filePath = Path.Combine(directoryPath, uniqueFilename);
				FileStatus fileStatus = repo.RetrieveStatus(filePath);
				if (fileStatus is FileStatus.ModifiedInWorkdir or FileStatus.NewInWorkdir)
				{
					commitFiles.Add(filePath);
					AnsiConsole.MarkupLine($"[yellow]{filePath.EscapeMarkup()}[/] has outstanding changes");
				}
			}
		}

		return commitFiles;
	}

	private static void CommitFile(string filePath)
	{
		AnsiConsole.MarkupLine($"[green]Committing:[/] {filePath.EscapeMarkup()}");
		string repoPath = Repository.Discover(filePath);
		if (string.IsNullOrEmpty(repoPath))
		{
			return;
		}

		using Repository repo = new(repoPath);
		string repoRoot = StripGitSuffix(repoPath);
		string relativeFilePath = filePath.Replace(repoRoot, "", StringComparison.Ordinal);
		repo.Index.Add(relativeFilePath);
		repo.Index.Write();
		try
		{
			Signature signature = new(CommitAuthorName, CommitAuthorName, DateTimeOffset.Now);
			_ = repo.Commit($"Sync {relativeFilePath}", signature, signature);
		}
		catch (EmptyCommitException)
		{
			// No changes to commit
		}
		catch (UnmergedIndexEntriesException)
		{
			AnsiConsole.MarkupLine($"[red]Unmerged entries in:[/] {filePath.EscapeMarkup()}");
		}
	}

	private async Task PushToRemoteAsync(HashSet<string> commitDirectories, string path, bool autoPush, CancellationToken ct)
	{
		Collection<string> pushDirectories = FindPushableDirectories(commitDirectories, path);

		if (pushDirectories.Count == 0)
		{
			return;
		}

		AnsiConsole.WriteLine();
		bool confirmed;
		if (autoPush)
		{
			AnsiConsole.MarkupLine("[dim]--auto-push enabled; all unpushed commits are by KtsuTools, pushing without prompting.[/]");
			confirmed = true;
		}
		else
		{
			confirmed = await AnsiConsole.ConfirmAsync("Push changes to remote?", defaultValue: false, cancellationToken: ct).ConfigureAwait(false);
		}

		if (!confirmed)
		{
			return;
		}

		AnsiConsole.WriteLine();
		foreach (string dir in pushDirectories)
		{
			ct.ThrowIfCancellationRequested();
			await PushDirectoryAsync(dir, ct).ConfigureAwait(false);
		}
	}

	private static Collection<string> FindPushableDirectories(HashSet<string> commitDirectories, string path)
	{
		Collection<string> pushDirectories = [];
		IEnumerable<string> commitRepos = commitDirectories
			.Select(f => Repository.Discover(Path.Combine(path, f)))
			.Where(r => !string.IsNullOrEmpty(r) && IsGitRepoPath(r))
			.Distinct();

		foreach (string repoPath in commitRepos)
		{
			using Repository repo = new(repoPath);
			string repoRoot = StripGitSuffix(repoPath);
			Branch localBranch = repo.Branches[repo.Head.FriendlyName];
			int aheadBy = localBranch?.TrackingDetails.AheadBy ?? 0;

			bool canPush = repo.Head.Commits
				.Take(aheadBy)
				.All(commit => commit.Author.Name == CommitAuthorName);

			if (aheadBy > 0 && canPush)
			{
				pushDirectories.Add(repoRoot);
				AnsiConsole.MarkupLine($"[cyan]{repoRoot.EscapeMarkup()}[/] can be pushed automatically");
			}
		}

		return pushDirectories;
	}

	private async Task PushDirectoryAsync(string repoRoot, CancellationToken ct)
	{
		AnsiConsole.MarkupLine($"[green]Pushing:[/] {repoRoot.EscapeMarkup()}");

		AnsiConsole.MarkupLine("[dim]Pulling remote changes...[/]");
		ProcessResult pull = await processService.RunAsync("git", "pull", repoRoot, ct).ConfigureAwait(false);
		if (pull.ExitCode != 0)
		{
			string pullMessage = pull.Errors.Count > 0 ? string.Join('\n', pull.Errors) : string.Join('\n', pull.Output);
			AnsiConsole.MarkupLine($"[red]Pull failed for:[/] {repoRoot.EscapeMarkup()}");
			AnsiConsole.MarkupLine($"[red]{pullMessage.EscapeMarkup()}[/]");
			AnsiConsole.MarkupLine("[yellow]Skipping push to avoid non-fast-forward; resolve conflicts manually.[/]");
			return;
		}

		ProcessResult push = await processService.RunAsync("git", "push", repoRoot, ct).ConfigureAwait(false);
		if (push.ExitCode == 0)
		{
			AnsiConsole.MarkupLine($"[green]Successfully pushed:[/] {repoRoot.EscapeMarkup()}");
		}
		else
		{
			string pushMessage = push.Errors.Count > 0 ? string.Join('\n', push.Errors) : string.Join('\n', push.Output);
			AnsiConsole.MarkupLine($"[red]Error pushing:[/] {pushMessage.EscapeMarkup()}");
		}
	}

	/// <summary>
	/// Converts a byte array hash to a hexadecimal string.
	/// </summary>
	/// <param name="array">The byte array to convert.</param>
	/// <returns>A hexadecimal string representation of the hash.</returns>
	internal static string HashToString(byte[] array)
	{
		StringBuilder builder = new();
		for (int i = 0; i < array.Length; i++)
		{
			_ = builder.Append(array[i].ToString("X2", CultureInfo.InvariantCulture));
		}

		return builder.ToString();
	}

	/// <summary>
	/// Checks if a directory path is inside a nested git repository (a repo inside another repo).
	/// </summary>
	/// <param name="directoryPath">The directory path to check.</param>
	/// <returns>True if the directory is inside a nested git repo.</returns>
	internal static bool IsRepoNested(AbsoluteDirectoryPath directoryPath)
	{
		AbsoluteDirectoryPath checkDir = directoryPath;
		bool foundFirstRepo = false;

		while (!checkDir.IsRoot)
		{
			string gitDirPath = Path.Combine(checkDir.ToString(), ".git");
			if (Directory.Exists(gitDirPath))
			{
				if (foundFirstRepo)
				{
					return true;
				}

				foundFirstRepo = true;
			}

			checkDir = checkDir.Parent;
		}

		return false;
	}
}
