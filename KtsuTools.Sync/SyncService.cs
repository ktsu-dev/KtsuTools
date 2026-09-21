// Copyright (c) 2023-2026 ktsu-dev contributors

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

	// Windows paths differ only in case, so two spellings of one directory are the same root there
	// and two different roots everywhere else.
	private static readonly StringComparer PathComparer =
		OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

	private static readonly StringComparison PathComparison =
		OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

	/// <summary>
	/// Records where a repository was before sync moved it onto the sync branch, so the original
	/// checkout can be restored and the sync's own commits can be told apart from what was there.
	/// </summary>
	/// <param name="RepoRoot">Working directory of the repository that was switched.</param>
	/// <param name="OriginalBranch">The branch to return to, or a commit sha when HEAD was detached.</param>
	/// <param name="OriginalTipSha">The tip the sync branch was based on.</param>
	internal sealed record BranchSwitch(string RepoRoot, string OriginalBranch, string OriginalTipSha);

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
	public Task<int> RunAsync(AbsoluteDirectoryPath path, IReadOnlyList<string> filenames, bool autoPush, CancellationToken ct = default) =>
		RunAsync(path, filenames, autoPush, branch: string.Empty, ct);

	/// <summary>
	/// Runs the sync operation for the specified path and one or more filename patterns.
	/// </summary>
	/// <param name="path">The root path to scan recursively.</param>
	/// <param name="filenames">One or more filename patterns to scan for.</param>
	/// <param name="autoPush">When true, repos whose unpushed commits are all authored by KtsuTools are pushed without prompting.</param>
	/// <param name="branch">When non-empty, commits land on a branch of this name in each repo, created if missing and reused if it already exists, and the original checkout is restored afterwards.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>Exit code (0 for success).</returns>
	public Task<int> RunAsync(AbsoluteDirectoryPath path, IReadOnlyList<string> filenames, bool autoPush, string branch, CancellationToken ct = default)
	{
		Ensure.NotNull(path);
		return RunAsync([path.ToString()], filenames, autoPush, branch, exclusions: [], ct);
	}

	/// <summary>
	/// Runs the sync operation over one or more roots, each either a workspace to walk or a single
	/// repository, skipping anything under an excluded directory.
	/// </summary>
	/// <param name="roots">The directories to scan recursively. Overlapping roots are scanned once.</param>
	/// <param name="filenames">One or more filename patterns to scan for.</param>
	/// <param name="autoPush">When true, repos whose unpushed commits are all authored by KtsuTools are pushed without prompting.</param>
	/// <param name="branch">When non-empty, commits land on a branch of this name in each repo, created if missing and reused if it already exists, and the original checkout is restored afterwards.</param>
	/// <param name="exclusions">Directory names, or paths, whose contents are left out of the scan.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>Exit code (0 for success).</returns>
	public async Task<int> RunAsync(
		IReadOnlyList<string> roots,
		IReadOnlyList<string> filenames,
		bool autoPush,
		string branch,
		IReadOnlyList<string> exclusions,
		CancellationToken ct = default)
	{
		Ensure.NotNull(roots);
		ct.ThrowIfCancellationRequested();

		List<string> scanRoots = [.. roots
			.Where(r => !string.IsNullOrWhiteSpace(r))
			.Select(NormalizeRoot)
			.Distinct(PathComparer)];

		if (scanRoots.Count == 0)
		{
			AnsiConsole.MarkupLine("[red]No paths to scan.[/]");
			return 1;
		}

		if (scanRoots.Find(r => !Directory.Exists(r)) is string missing)
		{
			AnsiConsole.MarkupLine($"[red]Path does not exist: {missing.EscapeMarkup()}[/]");
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

		IReadOnlyList<string> exclusionList = NormalizeExclusions(exclusions);

		AnsiConsole.MarkupLine($"[bold]Scanning for:[/] {string.Join(", ", patterns).EscapeMarkup()}");
		AnsiConsole.MarkupLine($"[bold]In:[/] {string.Join(", ", scanRoots).EscapeMarkup()}");
		if (exclusionList.Count > 0)
		{
			AnsiConsole.MarkupLine($"[bold]Excluding:[/] {string.Join(", ", exclusionList).EscapeMarkup()}");
		}

		AnsiConsole.WriteLine();

		Collection<string> fileEnumeration = [.. FindMatchingFiles(scanRoots, patterns, exclusionList)];

		IEnumerable<string> uniqueFilenames = fileEnumeration.Select(Path.GetFileName).Distinct()!;
		AnsiConsole.MarkupLine($"[bold]Found matches:[/] {string.Join(", ", uniqueFilenames).EscapeMarkup()}");

		HashSet<string> commitDirectories = [];
		HashSet<string> expandedFilesToSync = [];

		expandedFilesToSync.UnionWith(uniqueFilenames);

		foreach (string uniqueFilename in uniqueFilenames)
		{
			ct.ThrowIfCancellationRequested();
			await ProcessUniqueFilenameAsync(uniqueFilename, fileEnumeration, scanRoots, commitDirectories, ct).ConfigureAwait(false);
		}

		string branchName = branch?.Trim() ?? string.Empty;

		IReadOnlyList<BranchSwitch> branchSwitches =
			await CommitChangedFilesAsync(commitDirectories, expandedFilesToSync, branchName).ConfigureAwait(false);

		try
		{
			await PushToRemoteAsync(commitDirectories, autoPush, branchName, branchSwitches, ct).ConfigureAwait(false);
		}
		finally
		{
			RestoreBranches(branchSwitches);
		}

		return 0;
	}

	/// <summary>
	/// Resolves the directories to scan from a workspace path, explicit repository paths, and a file
	/// listing repository paths, in that order and without duplicates.
	/// </summary>
	/// <param name="path">The workspace to walk, or empty when only explicit repositories are given.</param>
	/// <param name="repos">Explicit repository paths, each of which may also be a comma-separated list.</param>
	/// <param name="repoListFile">A file listing repository paths, or empty when there is none.</param>
	/// <returns>The absolute roots to scan, in the order they were supplied.</returns>
	public static IReadOnlyList<string> ResolveRoots(string? path, IEnumerable<string>? repos, string? repoListFile)
	{
		List<string> roots = [];

		if (!string.IsNullOrWhiteSpace(path))
		{
			roots.Add(path);
		}

		if (repos is not null)
		{
			roots.AddRange(repos
				.Where(r => r is not null)
				.SelectMany(r => r.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
		}

		if (!string.IsNullOrWhiteSpace(repoListFile))
		{
			roots.AddRange(ReadRepoList(repoListFile));
		}

		return [.. roots
			.Where(r => !string.IsNullOrWhiteSpace(r))
			.Select(NormalizeRoot)
			.Distinct(PathComparer)];
	}

	/// <summary>
	/// Reads repository paths from a list file, one per line, ignoring blank lines and # comments.
	/// A relative entry resolves against the list file's own directory, so a list can travel with the
	/// checkout it describes rather than depending on where the tool was run from.
	/// </summary>
	/// <param name="repoListFile">Path of the file to read.</param>
	/// <returns>The absolute repository paths the file names.</returns>
	public static IReadOnlyList<string> ReadRepoList(string repoListFile)
	{
		string fullPath = Path.GetFullPath(repoListFile);
		string baseDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();

		return [.. File.ReadLines(fullPath)
			.Select(line => line.Trim())
			.Where(line => line.Length > 0 && !line.StartsWith('#'))
			.Select(line => Path.GetFullPath(line, baseDirectory))];
	}

	/// <summary>
	/// Splits comma-separated exclusions apart and drops the blanks and duplicates.
	/// </summary>
	/// <param name="exclusions">The exclusions as supplied on the command line.</param>
	/// <returns>The distinct exclusion entries.</returns>
	internal static IReadOnlyList<string> NormalizeExclusions(IEnumerable<string>? exclusions) =>
		exclusions is null
			? []
			: [.. exclusions
				.Where(e => e is not null)
				.SelectMany(e => e.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				.Where(e => !string.IsNullOrWhiteSpace(e))
				.Distinct(PathComparer)];

	/// <summary>
	/// Whether a matched file sits under an excluded directory. An exclusion naming a path excludes
	/// that directory alone; one naming a bare directory excludes every directory of that name.
	/// </summary>
	/// <param name="filePath">Absolute path of the matched file.</param>
	/// <param name="exclusions">The normalized exclusion entries.</param>
	/// <returns>True when the file is excluded.</returns>
	internal static bool IsExcluded(string filePath, IReadOnlyList<string> exclusions)
	{
		Ensure.NotNull(exclusions);

		if (exclusions.Count == 0)
		{
			return false;
		}

		string fullPath = Path.GetFullPath(filePath);
		string[] directorySegments = DirectorySegmentsOf(fullPath);

		foreach (string trimmed in exclusions.Select(Path.TrimEndingDirectorySeparator))
		{
			bool excluded = HasDirectorySeparator(trimmed)
				? fullPath.StartsWith(NormalizeRoot(trimmed) + Path.DirectorySeparatorChar, PathComparison)
				: Array.Exists(directorySegments, segment => string.Equals(segment, trimmed, PathComparison));

			if (excluded)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Every file matching any pattern under any root, skipping exclusions and nested repositories,
	/// with each file reported once however many roots reach it.
	/// </summary>
	/// <param name="roots">The absolute directories to scan recursively.</param>
	/// <param name="patterns">The filename patterns to match.</param>
	/// <param name="exclusions">The normalized exclusion entries.</param>
	/// <returns>Absolute paths of the matched files, in scan order.</returns>
	internal static IReadOnlyList<string> FindMatchingFiles(
		IReadOnlyList<string> roots,
		IReadOnlyList<string> patterns,
		IReadOnlyList<string> exclusions)
	{
		Ensure.NotNull(roots);
		Ensure.NotNull(patterns);

		HashSet<string> seen = new(PathComparer);
		List<string> matches = [];

		foreach (string file in EnumerateCandidates(roots, patterns).Where(file => IsScannable(file, exclusions)))
		{
			// Deduplication stays in the loop body: it is stateful, so it has to run in scan order.
			if (seen.Add(file))
			{
				matches.Add(file);
			}
		}

		return matches;
	}

	/// <summary>
	/// Every file under any root matching any pattern, in scan order, including the duplicates that
	/// overlapping roots and overlapping patterns produce.
	/// </summary>
	/// <param name="roots">The absolute directories to scan recursively.</param>
	/// <param name="patterns">The filename patterns to match.</param>
	/// <returns>Absolute paths of the matched files.</returns>
	private static IEnumerable<string> EnumerateCandidates(IReadOnlyList<string> roots, IReadOnlyList<string> patterns)
	{
		foreach (string root in roots)
		{
			foreach (string pattern in patterns)
			{
				foreach (string file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
				{
					yield return file;
				}
			}
		}
	}

	/// <summary>
	/// Whether a matched file is one the sync should take, which excludes anything under an excluded
	/// directory and anything inside a repository nested in another repository.
	/// </summary>
	/// <param name="file">Absolute path of the matched file.</param>
	/// <param name="exclusions">The normalized exclusion entries.</param>
	/// <returns>True when the file belongs in the scan.</returns>
	private static bool IsScannable(string file, IReadOnlyList<string> exclusions) =>
		!IsExcluded(file, exclusions)
		&& !IsRepoNested(AbsoluteFilePath.Create<AbsoluteFilePath>(file).AbsoluteDirectoryPath);

	/// <summary>
	/// The form of a matched directory to show. It stays relative while a single workspace is being
	/// scanned, and becomes absolute once several roots could produce the same relative path.
	/// </summary>
	/// <param name="directory">Absolute path of the directory to show.</param>
	/// <param name="roots">The roots being scanned.</param>
	/// <returns>The path to display.</returns>
	internal static string DisplayPath(string directory, IReadOnlyList<string> roots)
	{
		Ensure.NotNull(roots);

		if (roots.Count != 1)
		{
			return directory;
		}

		string root = roots[0];

		if (string.Equals(directory, root, PathComparison))
		{
			string name = Path.GetFileName(root);
			return name.Length > 0 ? name : root;
		}

		string prefix = root + Path.DirectorySeparatorChar;
		return directory.StartsWith(prefix, PathComparison)
			? directory[prefix.Length..]
			: directory;
	}

	private static string NormalizeRoot(string root) =>
		Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

	private static bool HasDirectorySeparator(string value) =>
		value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar);

	private static string[] DirectorySegmentsOf(string filePath)
	{
		string? directory = Path.GetDirectoryName(filePath);
		return string.IsNullOrEmpty(directory)
			? []
			: directory.Split(
				[Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
				StringSplitOptions.RemoveEmptyEntries);
	}

	private static async Task ProcessUniqueFilenameAsync(
		string uniqueFilename,
		Collection<string> fileEnumeration,
		IReadOnlyList<string> roots,
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

			result.Add(Path.GetDirectoryName(file) ?? string.Empty);
		}

		IEnumerable<string> allDirectories = results.SelectMany(r => r.Value);
		commitDirectories.UnionWith(allDirectories);

		if (results.Count > 1)
		{
			await HandleMultipleHashGroupsAsync(results, uniqueFilename, roots, ct).ConfigureAwait(false);
		}
		else if (results.Count == 1)
		{
			AnsiConsole.MarkupLine($"[green]All files in sync for:[/] {uniqueFilename.EscapeMarkup()}");
		}
	}

	private static async Task HandleMultipleHashGroupsAsync(
		Dictionary<string, Collection<string>> results,
		string uniqueFilename,
		IReadOnlyList<string> roots,
		CancellationToken ct)
	{
		Dictionary<string, DateTime> oldestModificationDates = CalculateOldestModificationDates(results, uniqueFilename);

		// Sort by oldest modification date (most recent first)
		Dictionary<string, Collection<string>> sortedResults = results
			.OrderByDescending(r => oldestModificationDates[r.Key])
			.ToDictionary(r => r.Key, r => r.Value);

		DisplayHashGroupsTable(sortedResults, uniqueFilename, oldestModificationDates, roots);

		string syncHash = PromptForSyncHash(sortedResults);

		if (!string.IsNullOrWhiteSpace(syncHash))
		{
			await SyncFilesToHashAsync(syncHash, sortedResults, uniqueFilename, roots, ct).ConfigureAwait(false);
		}
	}

	internal static Dictionary<string, DateTime> CalculateOldestModificationDates(
		Dictionary<string, Collection<string>> results,
		string uniqueFilename)
	{
		// Reduce the name to a bare file name rather than trusting the caller, and join instead of
		// combining: Path.Combine returns a rooted second argument on its own, discarding the
		// directory, where Path.Join always keeps both.
		string fileName = Path.GetFileName(uniqueFilename);

		Dictionary<string, DateTime> oldestModificationDates = [];
		foreach ((string hash, Collection<string> directories) in results)
		{
			DateTime oldestModified = directories
				.Min(dir => new FileInfo(Path.Join(dir, fileName)).LastWriteTime);

			oldestModificationDates[hash] = oldestModified;
		}

		return oldestModificationDates;
	}

	internal static void DisplayHashGroupsTable(
		Dictionary<string, Collection<string>> results,
		string uniqueFilename,
		Dictionary<string, DateTime> oldestModificationDates,
		IReadOnlyList<string> roots)
	{
		AnsiConsole.WriteLine();
		AnsiConsole.MarkupLine($"[bold yellow]Differences found for:[/] {uniqueFilename.EscapeMarkup()}");
		AnsiConsole.WriteLine();

		foreach ((string hash, Collection<string> directories) in results)
		{
			Table table = new()
			{
				Title = new TableTitle($"[bold]{hash.EscapeMarkup()}[/] ({oldestModificationDates[hash].ToString("g", CultureInfo.CurrentCulture).EscapeMarkup()})"),
			};
			table.AddColumn("Directory");
			table.Border(TableBorder.Rounded);

			foreach (string dir in directories)
			{
				table.AddRow(DisplayPath(dir, roots).EscapeMarkup());
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
		IReadOnlyList<string> roots,
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

		// Reduce the name to a bare file name, and join rather than combine. Path.Combine returns a
		// rooted second argument on its own, which here would make source and destination the same
		// path and copy the file over itself; Path.Join always keeps the directory.
		string fileName = Path.GetFileName(uniqueFilename);
		string sourceFile = Path.Join(sourceDir, fileName);

		AnsiConsole.MarkupLine("[bold]Planned copies:[/]");
		foreach (string dir in destinationDirectories)
		{
			string destinationFile = Path.Join(DisplayPath(dir, roots), fileName);
			AnsiConsole.MarkupLine($"  [blue]{DisplayPath(sourceDir, roots).EscapeMarkup()}[/] -> [yellow]{destinationFile.EscapeMarkup()}[/]");
		}

		AnsiConsole.WriteLine();

		bool confirmed = await AnsiConsole.ConfirmAsync("Proceed with sync?", defaultValue: false, ct).ConfigureAwait(false);

		if (confirmed)
		{
			AnsiConsole.WriteLine();
			foreach (string dir in destinationDirectories)
			{
				ct.ThrowIfCancellationRequested();
				string destinationFile = Path.Join(dir, fileName);
				AnsiConsole.MarkupLine($"[green]Copying:[/] {DisplayPath(sourceDir, roots).EscapeMarkup()} -> {DisplayPath(dir, roots).EscapeMarkup()}");
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

	private static async Task<IReadOnlyList<BranchSwitch>> CommitChangedFilesAsync(
		HashSet<string> commitDirectories,
		HashSet<string> expandedFilesToSync,
		string branchName)
	{
		AnsiConsole.WriteLine();

		Collection<string> commitFiles = FindChangedFiles(commitDirectories, expandedFilesToSync);

		if (commitFiles.Count == 0)
		{
			return [];
		}

		AnsiConsole.WriteLine();
		bool confirmed = await AnsiConsole.ConfirmAsync("Commit changed files?", defaultValue: false).ConfigureAwait(false);

		if (!confirmed)
		{
			return [];
		}

		AnsiConsole.WriteLine();
		return CommitFiles(commitFiles, branchName);
	}

	/// <summary>
	/// Commits the changed files, onto the sync branch in each repository when one is named.
	/// </summary>
	/// <param name="commitFiles">Absolute paths of the files to commit.</param>
	/// <param name="branchName">The sync branch, or empty to commit onto whatever is checked out.</param>
	/// <returns>The switches to undo once pushing is done, empty when committing in place.</returns>
	internal static IReadOnlyList<BranchSwitch> CommitFiles(IReadOnlyList<string> commitFiles, string branchName)
	{
		List<BranchSwitch> branchSwitches = [];
		IEnumerable<string> toCommit = commitFiles;

		if (!string.IsNullOrEmpty(branchName))
		{
			branchSwitches = [.. SwitchReposToBranch(commitFiles, branchName)];

			// A repo that could not be switched keeps its checked-out branch, which is exactly what
			// --branch exists to avoid, so its files are left uncommitted rather than landing there.
			HashSet<string> switched = new(branchSwitches.Select(s => s.RepoRoot), StringComparer.Ordinal);
			toCommit = commitFiles.Where(f => RepoRootFor(f) is string root && switched.Contains(root));
		}

		foreach (string filePath in toCommit)
		{
			CommitFile(filePath);
		}

		return branchSwitches;
	}

	internal static IEnumerable<BranchSwitch> SwitchReposToBranch(IEnumerable<string> commitFiles, string branchName)
	{
		foreach (string repoRoot in RepoRootsFor(commitFiles))
		{
			BranchSwitch? branchSwitch;
			try
			{
				branchSwitch = SwitchToSyncBranch(repoRoot, branchName);
			}
			catch (LibGit2SharpException ex)
			{
				AnsiConsole.MarkupLine($"[red]Could not check out {branchName.EscapeMarkup()} in:[/] {repoRoot.EscapeMarkup()}");
				AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
				continue;
			}

			if (branchSwitch is null)
			{
				AnsiConsole.MarkupLine($"[yellow]Skipping (no commits to branch from):[/] {repoRoot.EscapeMarkup()}");
				continue;
			}

			AnsiConsole.MarkupLine($"[green]On branch[/] {branchName.EscapeMarkup()} [green]in:[/] {repoRoot.EscapeMarkup()}");
			yield return branchSwitch;
		}
	}

	/// <summary>
	/// Distinct repository roots owning the supplied files, in first-seen order.
	/// </summary>
	/// <param name="filePaths">Absolute paths of files to resolve.</param>
	/// <returns>The repository working directories, without duplicates.</returns>
	internal static IReadOnlyList<string> RepoRootsFor(IEnumerable<string> filePaths) =>
	[
		.. filePaths
			.Select(RepoRootFor)
			.OfType<string>()
			.Distinct(StringComparer.Ordinal)
	];

	private static string? RepoRootFor(string filePath)
	{
		string repoPath = Repository.Discover(filePath);
		return string.IsNullOrEmpty(repoPath) || !IsGitRepoPath(repoPath)
			? null
			: StripGitSuffix(repoPath);
	}

	/// <summary>
	/// Checks out the sync branch in a repository, creating it at the current HEAD when it does not
	/// exist and reusing it when it does, and records what to restore afterwards.
	/// </summary>
	/// <param name="repoRoot">The repository working directory.</param>
	/// <param name="branchName">The branch to commit onto.</param>
	/// <returns>The recorded switch, or <see langword="null"/> when the repository has no commit to branch from.</returns>
	internal static BranchSwitch? SwitchToSyncBranch(string repoRoot, string branchName)
	{
		using Repository repo = new(repoRoot);

		if (repo.Head.Tip is null)
		{
			return null;
		}

		string originalTipSha = repo.Head.Tip.Sha;
		string original = repo.Info.IsHeadDetached ? originalTipSha : repo.Head.FriendlyName;

		if (!string.Equals(repo.Head.FriendlyName, branchName, StringComparison.Ordinal))
		{
			Branch target = repo.Branches[branchName] ?? repo.CreateBranch(branchName);
			_ = Commands.Checkout(repo, target);
		}

		return new BranchSwitch(repoRoot, original, originalTipSha);
	}

	/// <summary>
	/// Returns each repository to the branch it was on before the sync branch was checked out.
	/// </summary>
	/// <param name="branchSwitches">The switches recorded by <see cref="SwitchToSyncBranch"/>.</param>
	internal static void RestoreBranches(IReadOnlyList<BranchSwitch> branchSwitches)
	{
		foreach (BranchSwitch branchSwitch in branchSwitches)
		{
			try
			{
				RestoreBranch(branchSwitch);
			}
			catch (LibGit2SharpException ex)
			{
				AnsiConsole.MarkupLine($"[red]Could not restore {branchSwitch.OriginalBranch.EscapeMarkup()} in:[/] {branchSwitch.RepoRoot.EscapeMarkup()}");
				AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
			}
		}
	}

	/// <summary>
	/// Returns a single repository to the branch it was on before the sync branch was checked out.
	/// </summary>
	/// <param name="branchSwitch">The switch recorded by <see cref="SwitchToSyncBranch"/>.</param>
	internal static void RestoreBranch(BranchSwitch branchSwitch)
	{
		Ensure.NotNull(branchSwitch);

		using Repository repo = new(branchSwitch.RepoRoot);
		if (string.Equals(repo.Head.FriendlyName, branchSwitch.OriginalBranch, StringComparison.Ordinal))
		{
			return;
		}

		_ = Commands.Checkout(repo, branchSwitch.OriginalBranch);
	}

	/// <summary>
	/// Commits on the repository's current HEAD that are not reachable from the recorded base tip,
	/// which for a sync branch is exactly the commits the sync added.
	/// </summary>
	/// <param name="repoRoot">The repository working directory.</param>
	/// <param name="baseTipSha">The tip the sync branch was based on.</param>
	/// <returns>The author name of each commit added on top of the base, newest first.</returns>
	internal static IReadOnlyList<string> CommitAuthorsSinceBase(string repoRoot, string baseTipSha)
	{
		using Repository repo = new(repoRoot);

		CommitFilter filter = new() { IncludeReachableFrom = repo.Head };
		if (!string.IsNullOrEmpty(baseTipSha))
		{
			filter.ExcludeReachableFrom = baseTipSha;
		}

		return [.. repo.Commits.QueryBy(filter).Select(c => c.Author.Name)];
	}

	/// <summary>
	/// Builds the git push arguments, which must set the upstream when sync created the branch
	/// locally and the remote has never seen it.
	/// </summary>
	/// <param name="branchName">The sync branch, or empty when committing in place.</param>
	/// <returns>The arguments to pass to git.</returns>
	internal static string BuildPushArguments(string branchName) =>
		string.IsNullOrEmpty(branchName)
			? "push"
			: $"push --set-upstream origin {branchName}";

	private static Collection<string> FindChangedFiles(
		HashSet<string> commitDirectories,
		HashSet<string> expandedFilesToSync)
	{
		Collection<string> commitFiles = [];

		foreach (string directoryPath in commitDirectories)
		{
			string repoPath = Repository.Discover(directoryPath);
			if (repoPath is null || !IsGitRepoPath(repoPath))
			{
				continue;
			}

			using Repository repo = new(repoPath);
			foreach (string uniqueFilename in expandedFilesToSync)
			{
				string filePath = Path.Join(directoryPath, uniqueFilename);
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

	internal async Task PushToRemoteAsync(
		HashSet<string> commitDirectories,
		bool autoPush,
		string branchName,
		IReadOnlyList<BranchSwitch> branchSwitches,
		CancellationToken ct)
	{
		// A branch sync just created has no upstream, so AheadBy is zero and the tracking-based
		// check would never find anything to push; the recorded base tip answers it instead.
		Collection<string> pushDirectories = string.IsNullOrEmpty(branchName)
			? FindPushableDirectories(commitDirectories)
			: FindPushableBranchDirectories(branchSwitches);

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
			await PushDirectoryAsync(dir, branchName, ct).ConfigureAwait(false);
		}
	}

	internal static Collection<string> FindPushableBranchDirectories(IReadOnlyList<BranchSwitch> branchSwitches)
	{
		Collection<string> pushDirectories = [];

		foreach (BranchSwitch branchSwitch in branchSwitches)
		{
			IReadOnlyList<string> authors = CommitAuthorsSinceBase(branchSwitch.RepoRoot, branchSwitch.OriginalTipSha);

			if (authors.Count > 0 && authors.All(author => author == CommitAuthorName))
			{
				pushDirectories.Add(branchSwitch.RepoRoot);
				AnsiConsole.MarkupLine($"[cyan]{branchSwitch.RepoRoot.EscapeMarkup()}[/] can be pushed automatically");
			}
		}

		return pushDirectories;
	}

	private static Collection<string> FindPushableDirectories(HashSet<string> commitDirectories)
	{
		Collection<string> pushDirectories = [];
		IEnumerable<string> commitRepos = commitDirectories
			.Select(Repository.Discover)
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

	internal async Task PushDirectoryAsync(string repoRoot, string branchName, CancellationToken ct)
	{
		AnsiConsole.MarkupLine($"[green]Pushing:[/] {repoRoot.EscapeMarkup()}");

		// Pulling only makes sense for a branch that tracks a remote one. A sync branch may not
		// exist on the remote at all, and `git pull` there fails for want of tracking information.
		if (string.IsNullOrEmpty(branchName))
		{
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
		}

		ProcessResult push = await processService.RunAsync("git", BuildPushArguments(branchName), repoRoot, ct).ConfigureAwait(false);
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
