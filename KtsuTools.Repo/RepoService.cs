// Copyright (c) 2023-2026 ktsu-dev contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ktsu.KtsuTools.Test")]
// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Repo;

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using ktsu.Semantics.Paths;
using KtsuTools.Core.Services.Git;
using KtsuTools.Core.Services.Process;
using KtsuTools.Core.Services.Settings;
using KtsuTools.Core.UI;
using Spectre.Console;

/// <summary>
/// Represents a discovered solution with its projects and dependencies.
/// </summary>
public record SolutionInfo
{
	/// <summary>Gets the solution name.</summary>
	public required string Name { get; init; }

	/// <summary>Gets the solution file path.</summary>
	public required string Path { get; init; }

	/// <summary>Gets the project file paths in this solution.</summary>
	public Collection<string> Projects { get; init; } = [];
}

/// <summary>
/// A cached repository together with the cached solutions that live inside it.
/// </summary>
public sealed record RepositoryListing
{
	/// <summary>Gets the repository directory name.</summary>
	public required string Name { get; init; }

	/// <summary>Gets the repository directory path.</summary>
	public required string Path { get; init; }

	/// <summary>Gets the solution file paths found within this repository.</summary>
	public Collection<string> Solutions { get; init; } = [];
}

/// <summary>
/// The cached repositories with their solutions, plus any cached solution that sits outside every
/// cached repository.
/// </summary>
public sealed record RepositoryListingSet
{
	/// <summary>Gets the cached repositories, ordered by name.</summary>
	public Collection<RepositoryListing> Repositories { get; init; } = [];

	/// <summary>Gets cached solutions that no cached repository contains.</summary>
	public Collection<string> OrphanSolutions { get; init; } = [];
}

/// <summary>
/// Output shape for <see cref="RepoService.ListAsync"/>.
/// </summary>
public enum RepoListFormat
{
	/// <summary>A rendered console table.</summary>
	Table,

	/// <summary>Machine-readable JSON written verbatim to stdout.</summary>
	Json,
}

/// <summary>
/// Service for cross-repository operations.
/// </summary>
public class RepoService(IGitService gitService, IProcessService processService, ISettingsService? settingsService = null)
{
	private const string DotnetCommand = "dotnet";
	private const string GitCommand = "git";

	private readonly IGitService gitService = gitService;
	private readonly IProcessService processService = processService;
	private readonly ISettingsService settingsService = settingsService ?? new SettingsService();
	private RepoCacheSettings? cacheStore;

	// Spectre's ProgressTask is not thread-safe, and FetchAllAsync updates it from parallel workers.
	private readonly Lock fetchLock = new();

	/// <summary>
	/// Discovers git repositories in the given directory.
	/// </summary>
	public async Task<IReadOnlyList<string>> DiscoverRepositoriesAsync(AbsoluteDirectoryPath path, CancellationToken ct = default)
	{
		_ = gitService;
		Ensure.NotNull(path);

		string fullPath = path.ToString();

		if (!Directory.Exists(fullPath))
		{
			AnsiConsole.MarkupLine($"[red]Error: Directory '{fullPath.EscapeMarkup()}' does not exist.[/]");
			return [];
		}

		List<string> sortedRepos = [];

		await AnsiConsole.Status()
			.Spinner(Spinner.Known.Star)
			.StartAsync("Discovering repositories...", async ctx =>
			{
				sortedRepos = await WalkAndCacheAsync(fullPath, ct).ConfigureAwait(false);

				ctx.Status($"Found {sortedRepos.Count} repositories");
			}).ConfigureAwait(false);

		// Display results
		Table table = new();
		table.AddColumn("Repository");
		table.AddColumn("Status");
		table.Border = TableBorder.Rounded;

		foreach (string repo in sortedRepos)
		{
			string repoName = Path.GetFileName(repo);
			table.AddRow(repoName.EscapeMarkup(), "[green]found[/]");
		}

		AnsiConsole.Write(table);
		AnsiConsole.MarkupLine($"[green]Found {sortedRepos.Count} repositories.[/]");

		return sortedRepos;
	}

	/// <summary>
	/// Lists the cached repositories and the solutions discovered within each.
	/// </summary>
	/// <param name="path">Root directory to walk, used only when the cache is empty or <paramref name="refresh"/> is set.</param>
	/// <param name="refresh">When true, re-walks the filesystem and rewrites the cache before listing.</param>
	/// <param name="format">Whether to render a table or emit JSON.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>Zero when the listing was produced, one when a refresh was asked for but the path does not exist.</returns>
	/// <remarks>
	/// Discovery already walked the filesystem and wrote what it found to the cache, so listing reads
	/// that cache rather than walking again. <paramref name="refresh"/> is the way to pay for a walk
	/// deliberately; an empty cache forces one because there is nothing else to show.
	/// </remarks>
	public async Task<int> ListAsync(
		AbsoluteDirectoryPath path,
		bool refresh = false,
		RepoListFormat format = RepoListFormat.Table,
		CancellationToken ct = default)
	{
		Ensure.NotNull(path);

		RepoCacheSettings cache = GetCacheStore();

		if (refresh || cache.Repositories.Count == 0)
		{
			string fullPath = path.ToString();

			if (!Directory.Exists(fullPath))
			{
				ErrorDisplay.ShowError($"Directory '{fullPath}' does not exist.");
				return 1;
			}

			await WalkAndCacheAsync(fullPath, ct).ConfigureAwait(false);
		}

		RepositoryListingSet listing = GroupSolutionsByRepository(cache.Repositories, cache.Solutions);

		if (format == RepoListFormat.Json)
		{
			WriteListJson(listing);
		}
		else
		{
			WriteListTable(listing);
		}

		return 0;
	}

	/// <summary>
	/// Groups cached solution paths under the cached repository that contains them.
	/// </summary>
	/// <param name="repositories">Cached repository directories.</param>
	/// <param name="solutions">Cached solution file paths.</param>
	/// <returns>The repositories in name order, each with its solutions, plus solutions no repository claims.</returns>
	/// <remarks>
	/// The cache stores repositories and solutions as two flat lists, so containment is decided by
	/// path prefix. A solution inside a repository that is itself nested in another is attributed to
	/// the nearest enclosing repository, matching how discovery treats nested repositories.
	/// </remarks>
	public static RepositoryListingSet GroupSolutionsByRepository(IEnumerable<string> repositories, IEnumerable<string> solutions)
	{
		Ensure.NotNull(repositories);
		Ensure.NotNull(solutions);

		List<string> repoPaths = [.. repositories];
		Dictionary<string, RepositoryListing> byPath = new(StringComparer.OrdinalIgnoreCase);
		RepositoryListingSet set = new();

		foreach (string repo in repoPaths.OrderBy(r => Path.GetFileName(TrimSeparators(r)), StringComparer.OrdinalIgnoreCase))
		{
			if (byPath.ContainsKey(repo))
			{
				continue;
			}

			RepositoryListing listing = new()
			{
				Name = Path.GetFileName(TrimSeparators(repo)),
				Path = repo,
			};

			byPath[repo] = listing;
			set.Repositories.Add(listing);
		}

		// Deepest path first, so a solution in a nested repository is claimed by that repository
		// rather than by the outer one that also contains it.
		List<string> deepestFirst = [.. byPath.Keys.OrderByDescending(r => TrimSeparators(r).Length)];

		foreach (string solution in solutions.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
		{
			string? owner = deepestFirst.FirstOrDefault(repo => RepositoryContains(repo, solution));

			if (owner is null)
			{
				set.OrphanSolutions.Add(solution);
				continue;
			}

			byPath[owner].Solutions.Add(solution);
		}

		return set;
	}

	private static string TrimSeparators(string path) =>
		path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

	private static bool RepositoryContains(string repository, string solution)
	{
		string prefix = TrimSeparators(repository);

		// The separator check is what stops 'c:/dev/Repo' from claiming 'c:/dev/RepoTools/X.sln'.
		return solution.Length > prefix.Length &&
			solution.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
			(solution[prefix.Length] == Path.DirectorySeparatorChar || solution[prefix.Length] == Path.AltDirectorySeparatorChar);
	}

	private static void WriteListTable(RepositoryListingSet listing)
	{
		if (listing.Repositories.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No repositories cached. Run 'ktsu repo discover', or pass --refresh.[/]");
			return;
		}

		Table table = new()
		{
			Border = TableBorder.Rounded,
		};

		table.AddColumn("Repository");
		table.AddColumn("Solutions");
		table.AddColumn(new TableColumn("Count").RightAligned());

		foreach (RepositoryListing repo in listing.Repositories)
		{
			string solutions = repo.Solutions.Count == 0
				? "[dim]none[/]"
				: string.Join(Environment.NewLine, repo.Solutions.Select(s => Path.GetFileName(s).EscapeMarkup()));

			table.AddRow(
				repo.Name.EscapeMarkup(),
				solutions,
				repo.Solutions.Count.ToString(CultureInfo.InvariantCulture));
		}

		AnsiConsole.Write(table);

		int solutionCount = listing.Repositories.Sum(repo => repo.Solutions.Count);
		AnsiConsole.MarkupLine($"[green]{listing.Repositories.Count} repositories · {solutionCount} solutions.[/]");

		if (listing.OrphanSolutions.Count > 0)
		{
			AnsiConsole.MarkupLine($"[yellow]{listing.OrphanSolutions.Count} cached solution(s) outside every cached repository:[/]");

			foreach (string solution in listing.OrphanSolutions)
			{
				AnsiConsole.MarkupLine($"  [dim]-[/] {solution.EscapeMarkup()}");
			}
		}
	}

	// Straight to stdout rather than through AnsiConsole, which wraps at the console width and would
	// try to read any [square brackets] in a path as Spectre markup.
	private static void WriteListJson(RepositoryListingSet listing) =>
		Console.Out.WriteLine(JsonSerializer.Serialize(listing, ListJsonOptions));

	private static readonly JsonSerializerOptions ListJsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	/// <summary>
	/// Validates the cached repository and solution paths, reporting and optionally pruning stale entries.
	/// </summary>
	/// <param name="dryRun">When true, reports stale cache entries without pruning them.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>Zero when validation completed.</returns>
	public async Task<int> ValidateCacheAsync(bool dryRun = false, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		RepoCacheSettings cache = GetCacheStore();
		List<string> staleRepos = [.. cache.Repositories.Where(path => !Directory.Exists(path))];
		List<string> staleSolutions = [.. cache.Solutions.Where(path => !File.Exists(path))];

		if (staleRepos.Count == 0 && staleSolutions.Count == 0)
		{
			AnsiConsole.MarkupLine($"[green]Cache is valid.[/] Repositories: {cache.Repositories.Count}, solutions: {cache.Solutions.Count}.");
			return 0;
		}

		Table table = new()
		{
			Border = TableBorder.Rounded,
		};
		table.AddColumn("Type");
		table.AddColumn("Path");
		table.AddColumn("Status");

		foreach (string path in staleRepos.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
		{
			table.AddRow("Repository", path.EscapeMarkup(), "[yellow]stale[/]");
		}

		foreach (string path in staleSolutions.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
		{
			table.AddRow("Solution", path.EscapeMarkup(), "[yellow]stale[/]");
		}

		AnsiConsole.Write(table);

		int staleRepoCount = staleRepos.Count;
		int staleSolutionCount = staleSolutions.Count;
		int remainingRepos = cache.Repositories.Count - staleRepoCount;
		int remainingSolutions = cache.Solutions.Count - staleSolutionCount;

		if (dryRun)
		{
			AnsiConsole.MarkupLine(
				$"[yellow]Dry run:[/] would prune {staleRepoCount} repository entr{(staleRepoCount == 1 ? "y" : "ies")} and {staleSolutionCount} solution entr{(staleSolutionCount == 1 ? "y" : "ies")}.");
			AnsiConsole.MarkupLine($"[blue]Would remain:[/] {remainingRepos} repositories, {remainingSolutions} solutions.");
			return 0;
		}

		foreach (string path in staleRepos)
		{
			_ = cache.Repositories.Remove(path);
		}

		foreach (string path in staleSolutions)
		{
			_ = cache.Solutions.Remove(path);
		}

		await settingsService.SaveAsync(cache).ConfigureAwait(false);

		AnsiConsole.MarkupLine(
			$"[green]Pruned {staleRepoCount} repository entr{(staleRepoCount == 1 ? "y" : "ies")} and {staleSolutionCount} solution entr{(staleSolutionCount == 1 ? "y" : "ies")}.[/]");
		AnsiConsole.MarkupLine($"[blue]Remaining:[/] {cache.Repositories.Count} repositories, {cache.Solutions.Count} solutions.");

		return 0;
	}

	/// <summary>
	/// Builds and tests all solutions found in repositories under the given path.
	/// </summary>
	public async Task<int> BuildAndTestAsync(AbsoluteDirectoryPath path, bool parallel = false, CancellationToken ct = default)
	{
		Ensure.NotNull(path);

		string fullPath = path.ToString();
		List<string> solutionFiles = DiscoverSolutionFiles(fullPath);

		if (solutionFiles.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No solution files found.[/]");
			return 0;
		}

		AnsiConsole.MarkupLine($"[blue]Found {solutionFiles.Count} solution(s).[/]");

		ConcurrentBag<string> failedSolutions = [];
		object consoleLock = new();

		await AnsiConsole.Progress()
			.AutoClear(false)
			.HideCompleted(false)
			.StartAsync(async progressContext =>
			{
				ProgressTask task = progressContext.AddTask("[green]Building and testing[/]", maxValue: solutionFiles.Count);

				if (parallel)
				{
					// dotnet build already uses multiple cores internally; half of ProcessorCount keeps us from thrashing.
					int dop = Math.Max(1, Environment.ProcessorCount / 2);
					ParallelOptions options = new() { CancellationToken = ct, MaxDegreeOfParallelism = dop };

					await Parallel.ForEachAsync(solutionFiles, options, async (sln, token) =>
						await BuildAndTestSolutionAsync(sln, fullPath, task, consoleLock, failedSolutions, token).ConfigureAwait(false))
						.ConfigureAwait(false);
				}
				else
				{
					foreach (string sln in solutionFiles)
					{
						ct.ThrowIfCancellationRequested();
						await BuildAndTestSolutionAsync(sln, fullPath, task, consoleLock, failedSolutions, ct).ConfigureAwait(false);
					}
				}
			}).ConfigureAwait(false);

		int failCount = failedSolutions.Count;

		if (failCount > 0)
		{
			AnsiConsole.MarkupLine($"[yellow]Done. {failCount} solution(s) had failures:[/]");
			foreach (string name in failedSolutions.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
			{
				AnsiConsole.MarkupLine($"  [red]-[/] {name.EscapeMarkup()}");
			}
		}
		else
		{
			AnsiConsole.MarkupLine("[green]Done. All solutions built and tested successfully.[/]");
		}

		return failCount > 0 ? 1 : 0;
	}

	private async Task BuildAndTestSolutionAsync(
		string sln,
		string fullPath,
		ProgressTask task,
		object consoleLock,
		ConcurrentBag<string> failedSolutions,
		CancellationToken ct)
	{
		string slnDir = Path.GetDirectoryName(sln) ?? fullPath;
		string slnName = Path.GetFileNameWithoutExtension(sln);

		lock (consoleLock)
		{
			task.Description = $"[green]Building {slnName.EscapeMarkup()}[/]";
		}

		ProcessResult buildResult = await processService.RunAsync(DotnetCommand, "build --nologo -v q", slnDir, ct).ConfigureAwait(false);

		if (buildResult.ExitCode != 0)
		{
			lock (consoleLock)
			{
				AnsiConsole.MarkupLine($"  [red]FAIL[/] {slnName.EscapeMarkup()} - build failed");
				task.Increment(1);
			}

			failedSolutions.Add(slnName);
			return;
		}

		ProcessResult testResult = await processService.RunAsync(DotnetCommand, "test --nologo --no-build -v q", slnDir, ct).ConfigureAwait(false);

		lock (consoleLock)
		{
			if (testResult.ExitCode != 0)
			{
				AnsiConsole.MarkupLine($"  [yellow]WARN[/] {slnName.EscapeMarkup()} - build OK, tests failed");
			}
			else
			{
				AnsiConsole.MarkupLine($"  [green]OK[/]   {slnName.EscapeMarkup()}");
			}

			task.Increment(1);
		}

		if (testResult.ExitCode != 0)
		{
			failedSolutions.Add(slnName);
		}
	}

	/// <summary>
	/// Pulls all repositories under the given path.
	/// </summary>
	public async Task<int> PullAllAsync(AbsoluteDirectoryPath path, CancellationToken ct = default)
	{
		_ = gitService;
		Ensure.NotNull(path);

		string fullPath = path.ToString();
		ConcurrentBag<string> repos = [];
		DiscoverGitReposRecursive(fullPath, repos);

		List<string> sortedRepos = [.. repos.OrderBy(r => Path.GetFileName(r), StringComparer.OrdinalIgnoreCase)];

		if (sortedRepos.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No repositories found.[/]");
			return 0;
		}

		AnsiConsole.MarkupLine($"[blue]Pulling {sortedRepos.Count} repositories...[/]");

		int failCount = 0;

		await AnsiConsole.Progress()
			.AutoClear(false)
			.HideCompleted(false)
			.StartAsync(async progressContext =>
			{
				ProgressTask task = progressContext.AddTask("[green]Pulling repositories[/]", maxValue: sortedRepos.Count);

				foreach (string repo in sortedRepos)
				{
					ct.ThrowIfCancellationRequested();

					string repoName = Path.GetFileName(repo);
					task.Description = $"[green]Pulling {repoName.EscapeMarkup()}[/]";

					ProcessResult result = await processService.RunAsync(GitCommand, "pull --all --autostash", repo, ct).ConfigureAwait(false);

					bool hasError = result.Errors.Any(line =>
						line.Contains("error:", StringComparison.OrdinalIgnoreCase) ||
						line.Contains("fatal:", StringComparison.OrdinalIgnoreCase) ||
						line.Contains("CONFLICT", StringComparison.Ordinal));

					if (result.ExitCode != 0 || hasError)
					{
						AnsiConsole.MarkupLine($"  [red]FAIL[/] {repoName.EscapeMarkup()}");
						failCount++;
					}
					else
					{
						AnsiConsole.MarkupLine($"  [green]OK[/]   {repoName.EscapeMarkup()}");
					}

					task.Increment(1);
				}
			}).ConfigureAwait(false);

		AnsiConsole.MarkupLine(failCount > 0
			? $"[yellow]Done. {failCount} repo(s) had errors.[/]"
			: "[green]Done. All repositories pulled successfully.[/]");

		return failCount > 0 ? 1 : 0;
	}

	/// <summary>
	/// Fetches every repository under the given path without touching any working tree, and reports
	/// how far each one has diverged from its upstream.
	/// </summary>
	/// <param name="path">Directory to search. Repositories are found recursively.</param>
	/// <param name="parallel">
	/// When true (the default) repositories are fetched concurrently. Fetching is a read-only
	/// network operation, so unlike <see cref="BuildAndTestAsync"/> there is nothing to serialize.
	/// </param>
	/// <param name="ct">Cancels before the next repository starts.</param>
	/// <returns>Zero when every repository fetched cleanly, otherwise one.</returns>
	/// <remarks>
	/// This is the survey counterpart to <see cref="PullAllAsync"/>: <c>git pull --autostash</c>
	/// stashes and merges, this only updates remote-tracking refs, so it is safe to run across a
	/// workspace with uncommitted work in it.
	/// </remarks>
	public async Task<int> FetchAllAsync(AbsoluteDirectoryPath path, bool parallel = true, CancellationToken ct = default)
	{
		_ = gitService;
		Ensure.NotNull(path);

		string fullPath = path.ToString();

		if (!Directory.Exists(fullPath))
		{
			ErrorDisplay.ShowError($"Directory '{fullPath}' does not exist.");
			return 1;
		}

		ConcurrentBag<string> repos = [];
		DiscoverGitReposRecursive(fullPath, repos);

		List<string> sortedRepos = [.. repos.OrderBy(r => Path.GetFileName(r), StringComparer.OrdinalIgnoreCase)];

		if (sortedRepos.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No repositories found.[/]");
			return 0;
		}

		ConcurrentDictionary<string, FetchOutcome> outcomes = [];

		await AnsiConsole.Progress()
			.AutoClear(false)
			.HideCompleted(false)
			.StartAsync(async progressContext =>
			{
				ProgressTask task = progressContext.AddTask("[green]Fetching repositories[/]", maxValue: sortedRepos.Count);

				if (parallel)
				{
					ParallelOptions options = new() { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount };

					await Parallel.ForEachAsync(sortedRepos, options, async (repo, token) =>
						outcomes[repo] = await FetchRepositoryAsync(repo, task, token).ConfigureAwait(false))
						.ConfigureAwait(false);
				}
				else
				{
					foreach (string repo in sortedRepos)
					{
						ct.ThrowIfCancellationRequested();
						outcomes[repo] = await FetchRepositoryAsync(repo, task, ct).ConfigureAwait(false);
					}
				}
			}).ConfigureAwait(false);

		WriteFetchTable(sortedRepos, outcomes);

		int failCount = outcomes.Values.Count(outcome => outcome.Failed);
		WriteRunSummary(sortedRepos.Count, failCount);

		return failCount > 0 ? 1 : 0;
	}

	private async Task<FetchOutcome> FetchRepositoryAsync(string repo, ProgressTask task, CancellationToken ct)
	{
		string repoName = Path.GetFileName(repo);

		// --prune drops remote-tracking refs for branches deleted upstream, so the counts below never
		// describe a branch that is no longer there. It rewrites no working tree.
		ProcessResult fetchResult = await processService.RunAsync(GitCommand, "fetch --all --prune", repo, ct).ConfigureAwait(false);

		bool failed = fetchResult.ExitCode != 0;

		lock (fetchLock)
		{
			task.Description = $"[green]Fetching {repoName.EscapeMarkup()}[/]";
			task.Increment(1);
		}

		if (failed)
		{
			return new FetchOutcome(Failed: true, Divergence: null);
		}

		ProcessResult countResult = await processService
			.RunAsync(GitCommand, "rev-list --left-right --count HEAD...@{upstream}", repo, ct)
			.ConfigureAwait(false);

		// A repository with no upstream exits non-zero here, which is not a fetch failure.
		AheadBehind? divergence = countResult.ExitCode == 0 ? AheadBehind.Parse(countResult.Output) : null;

		return new FetchOutcome(Failed: false, Divergence: divergence);
	}

	private static void WriteFetchTable(IReadOnlyList<string> sortedRepos, IReadOnlyDictionary<string, FetchOutcome> outcomes)
	{
		Table table = new()
		{
			Border = TableBorder.Rounded,
		};

		table.AddColumn("Repository");
		table.AddColumn("Fetch");
		table.AddColumn("Upstream");

		foreach (string repo in sortedRepos)
		{
			FetchOutcome outcome = outcomes.TryGetValue(repo, out FetchOutcome? found)
				? found
				: new FetchOutcome(Failed: true, Divergence: null);

			table.AddRow(
				Path.GetFileName(repo).EscapeMarkup(),
				outcome.Failed ? "[red]failed[/]" : "[green]ok[/]",
				AheadBehind.Render(outcome.Divergence).EscapeMarkup());
		}

		AnsiConsole.Write(table);
	}

	private sealed record FetchOutcome(bool Failed, AheadBehind? Divergence);

	/// <summary>
	/// The first line of a Git LFS pointer file. A working tree that still holds these instead of the
	/// real content is what a missing set of LFS filters looks like from the outside.
	/// </summary>
	private const string LfsPointerHeader = "version https://git-lfs.github.com/spec/v1";

	/// <summary>
	/// Pointer files are a few hundred bytes. Anything larger is the real content, so there is no
	/// reason to read it.
	/// </summary>
	private const long LfsPointerMaxBytes = 1024;

	/// <summary>How <c>git lfs install --local</c> went for one repository.</summary>
	internal enum LfsInstallStatus
	{
		/// <summary>The filters were configured.</summary>
		Installed,

		/// <summary>Git has no <c>lfs</c> subcommand here, so there was nothing to configure.</summary>
		Unavailable,

		/// <summary>Git has <c>lfs</c>, but the install exited non-zero.</summary>
		Failed,
	}

	/// <param name="Status">How the install went.</param>
	/// <param name="UnsmudgedPointers">
	/// Tracked files still sitting in the working tree as pointers. Only counted after a successful
	/// install, since the listing needs <c>git lfs</c> to work.
	/// </param>
	private sealed record LfsOutcome(LfsInstallStatus Status, int UnsmudgedPointers);

	/// <summary>
	/// Configures Git LFS in every repository found under the given path, so a freshly cloned
	/// workspace gets its filters in one pass instead of one repository at a time, on first failure.
	/// </summary>
	/// <param name="path">Directory to search. Repositories are found recursively.</param>
	/// <param name="ct">Cancels before the next repository starts.</param>
	/// <returns>
	/// Zero when no repository failed outright. A repository where <c>git lfs</c> is simply not
	/// installed is reported rather than counted as a failure, because that is one missing tool
	/// rather than a broken repository, and it would otherwise fail every row of the run.
	/// </returns>
	/// <remarks>
	/// Installing is only half the symptom. A repository cloned without the filters has pointer files
	/// in its working tree, and installing the filters afterwards does not rewrite them — that needs a
	/// checkout. Those files are counted per repository so the run says which repositories still need
	/// one.
	/// </remarks>
	public async Task<int> InstallLfsAsync(AbsoluteDirectoryPath path, CancellationToken ct = default)
	{
		_ = gitService;
		Ensure.NotNull(path);

		string fullPath = path.ToString();

		if (!Directory.Exists(fullPath))
		{
			ErrorDisplay.ShowError($"Directory '{fullPath}' does not exist.");
			return 1;
		}

		ConcurrentBag<string> repos = [];
		DiscoverGitReposRecursive(fullPath, repos);

		List<string> sortedRepos = [.. repos.OrderBy(r => Path.GetFileName(r), StringComparer.OrdinalIgnoreCase)];

		if (sortedRepos.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No repositories found.[/]");
			return 0;
		}

		Dictionary<string, LfsOutcome> outcomes = [];

		await AnsiConsole.Progress()
			.AutoClear(false)
			.HideCompleted(false)
			.StartAsync(async progressContext =>
			{
				ProgressTask task = progressContext.AddTask("[green]Installing Git LFS[/]", maxValue: sortedRepos.Count);

				foreach (string repo in sortedRepos)
				{
					ct.ThrowIfCancellationRequested();

					task.Description = $"[green]Installing Git LFS in {Path.GetFileName(repo).EscapeMarkup()}[/]";
					outcomes[repo] = await InstallLfsInRepositoryAsync(repo, ct).ConfigureAwait(false);
					task.Increment(1);
				}
			}).ConfigureAwait(false);

		WriteLfsTable(sortedRepos, outcomes);
		WriteLfsSummary(sortedRepos.Count, outcomes);

		return outcomes.Values.Any(outcome => outcome.Status == LfsInstallStatus.Failed) ? 1 : 0;
	}

	private async Task<LfsOutcome> InstallLfsInRepositoryAsync(string repo, CancellationToken ct)
	{
		// --local keeps the filters in the repository's own config, so nothing is written to the
		// user's global config on their behalf.
		ProcessResult install = await processService.RunAsync(GitCommand, "lfs install --local", repo, ct).ConfigureAwait(false);

		if (install.ExitCode != 0)
		{
			return new LfsOutcome(
				IsLfsUnavailable(install) ? LfsInstallStatus.Unavailable : LfsInstallStatus.Failed,
				UnsmudgedPointers: 0);
		}

		ProcessResult tracked = await processService.RunAsync(GitCommand, "lfs ls-files --name-only", repo, ct).ConfigureAwait(false);

		// A repository that tracks nothing through LFS exits non-zero on some git-lfs versions, and
		// either way has no pointers to count.
		int pointers = tracked.ExitCode == 0 ? CountUnsmudgedPointers(repo, tracked.Output) : 0;

		return new LfsOutcome(LfsInstallStatus.Installed, pointers);
	}

	/// <summary>
	/// Tells "git has no lfs subcommand" apart from a real install failure. Git answers an unknown
	/// subcommand with <c>git: 'lfs' is not a git command</c>; a shell that cannot find the binary at
	/// all says <c>command not found</c>.
	/// </summary>
	private static bool IsLfsUnavailable(ProcessResult result) =>
		result.Errors.Concat(result.Output).Any(line =>
			line.Contains("is not a git command", StringComparison.OrdinalIgnoreCase) ||
			line.Contains("lfs: command not found", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Counts how many of a repository's LFS-tracked files are still pointer files in the working
	/// tree, which is the state that breaks builds without announcing itself as an LFS problem.
	/// </summary>
	/// <param name="repoRoot">Working directory of the repository.</param>
	/// <param name="trackedPaths">Repository-relative paths, as <c>git lfs ls-files --name-only</c> prints them.</param>
	/// <returns>The number of tracked files whose content is a pointer rather than the real file.</returns>
	internal static int CountUnsmudgedPointers(string repoRoot, IEnumerable<string> trackedPaths) =>
		trackedPaths
			.Select(line => line.Trim())
			.Where(line => !string.IsNullOrEmpty(line))
			.Count(relative => IsPointerFile(Path.Combine(repoRoot, relative)));

	private static bool IsPointerFile(string file)
	{
		try
		{
			FileInfo info = new(file);

			if (!info.Exists || info.Length > LfsPointerMaxBytes)
			{
				return false;
			}

			using StreamReader reader = new(file);
			string? first = reader.ReadLine();

			return first is not null && first.StartsWith(LfsPointerHeader, StringComparison.Ordinal);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// An unreadable file is not evidence of a pointer, and one of them should not end the run.
			return false;
		}
	}

	private static void WriteLfsTable(IReadOnlyList<string> sortedRepos, IReadOnlyDictionary<string, LfsOutcome> outcomes)
	{
		Table table = new()
		{
			Border = TableBorder.Rounded,
		};

		table.AddColumn("Repository");
		table.AddColumn("Git LFS");
		table.AddColumn("Pointer files");

		foreach (string repo in sortedRepos)
		{
			LfsOutcome outcome = outcomes.TryGetValue(repo, out LfsOutcome? found)
				? found
				: new LfsOutcome(LfsInstallStatus.Failed, UnsmudgedPointers: 0);

			table.AddRow(
				Path.GetFileName(repo).EscapeMarkup(),
				RenderLfsStatus(outcome.Status),
				RenderPointerCount(outcome));
		}

		AnsiConsole.Write(table);
	}

	private static string RenderLfsStatus(LfsInstallStatus status) => status switch
	{
		LfsInstallStatus.Installed => "[green]installed[/]",
		LfsInstallStatus.Unavailable => "[yellow]unavailable[/]",
		_ => "[red]failed[/]",
	};

	private static string RenderPointerCount(LfsOutcome outcome)
	{
		if (outcome.Status != LfsInstallStatus.Installed)
		{
			return "-";
		}

		return outcome.UnsmudgedPointers > 0
			? $"[yellow]{outcome.UnsmudgedPointers} unsmudged[/]"
			: "[green]ok[/]";
	}

	private static void WriteLfsSummary(int repoCount, IReadOnlyDictionary<string, LfsOutcome> outcomes)
	{
		AnsiConsole.Write(new Rule().LeftJustified());

		int installed = outcomes.Values.Count(o => o.Status == LfsInstallStatus.Installed);
		int unavailable = outcomes.Values.Count(o => o.Status == LfsInstallStatus.Unavailable);
		int failed = outcomes.Values.Count(o => o.Status == LfsInstallStatus.Failed);

		string color = failed > 0 ? "red" : unavailable > 0 ? "yellow" : "green";
		AnsiConsole.MarkupLine($"[{color}]{repoCount} repos · {installed} installed · {unavailable} unavailable · {failed} failed[/]");

		if (unavailable > 0)
		{
			AnsiConsole.MarkupLine("[yellow]git lfs is not available. Install it from https://git-lfs.com and run this again.[/]");
		}

		int withPointers = outcomes.Values.Count(o => o.UnsmudgedPointers > 0);
		if (withPointers > 0)
		{
			AnsiConsole.MarkupLine($"[yellow]{withPointers} repo(s) still hold pointer files. Run 'git lfs pull' in each to replace them with the real content.[/]");
		}
	}

	/// <summary>
	/// Runs a git command in every repository found under the given path, writing each repository's
	/// output verbatim beneath a header.
	/// </summary>
	/// <param name="path">
	/// Directory to search. Repositories are found recursively, and a repository is never descended
	/// into, so a path that is itself a repository runs the command only there.
	/// </param>
	/// <param name="args">
	/// The git command line, already split into arguments, such as <c>["fetch", "--prune"]</c>.
	/// Arguments containing spaces are quoted before they reach git.
	/// </param>
	/// <param name="color">
	/// When true, forces git to colorize even though its output is redirected. Pass false for output
	/// that will be piped or diffed.
	/// </param>
	/// <param name="ct">Cancels before the next repository starts. The repository in flight finishes.</param>
	/// <returns>Zero when every repository exited zero, otherwise one.</returns>
	/// <remarks>
	/// Repositories run one at a time so that each block of output stays under its own header. A
	/// repository that fails does not stop the ones after it.
	/// </remarks>
	public async Task<int> RunGitAsync(AbsoluteDirectoryPath path, IReadOnlyList<string> args, bool color = true, CancellationToken ct = default)
	{
		_ = gitService;
		Ensure.NotNull(path);
		Ensure.NotNull(args);

		if (args.Count == 0)
		{
			ErrorDisplay.ShowError("Specify a git command, for example: ktsu git status");
			return 1;
		}

		string fullPath = path.ToString();

		if (!Directory.Exists(fullPath))
		{
			ErrorDisplay.ShowError($"Directory '{fullPath}' does not exist.");
			return 1;
		}

		ConcurrentBag<string> repos = [];
		DiscoverGitReposRecursive(fullPath, repos);

		List<string> sortedRepos = [.. repos.OrderBy(r => Path.GetFileName(r), StringComparer.OrdinalIgnoreCase)];

		if (sortedRepos.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No repositories found.[/]");
			return 0;
		}

		string arguments = BuildGitArguments(args, color);
		int failCount = 0;

		foreach (string repo in sortedRepos)
		{
			ct.ThrowIfCancellationRequested();

			ProcessResult result = await processService.RunAsync(GitCommand, arguments, repo, ct).ConfigureAwait(false);
			WriteRepoOutput(Path.GetFileName(repo), result);

			if (result.ExitCode != 0)
			{
				failCount++;
			}
		}

		WriteRunSummary(sortedRepos.Count, failCount);

		return failCount > 0 ? 1 : 0;
	}

	private static string BuildGitArguments(IReadOnlyList<string> args, bool color)
	{
		List<string> parts = [];

		if (color)
		{
			// git only honours -c before the subcommand, and color.ui=always is what makes it
			// colorize despite stdout being redirected into ProcessResult.
			parts.Add("-c");
			parts.Add("color.ui=always");
		}

		parts.AddRange(args);

		return string.Join(' ', parts.Select(QuoteIfNeeded));
	}

	private static string QuoteIfNeeded(string arg)
	{
		bool needsQuotes = arg.Length == 0 || arg.Any(char.IsWhiteSpace) || arg.Contains('"', StringComparison.Ordinal);

		return needsQuotes
			? $"\"{arg.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
			: arg;
	}

	private static void WriteRepoOutput(string repoName, ProcessResult result)
	{
		string color = result.ExitCode == 0 ? "blue" : "red";
		AnsiConsole.Write(new Rule($"[{color}]{repoName.EscapeMarkup()}[/]").LeftJustified());

		// git's output goes straight to stdout rather than through AnsiConsole, which would strip the
		// ANSI sequences we asked git for and try to parse any [square brackets] in a branch name,
		// file path, or commit message as Spectre markup.
		bool wroteAnything = false;

		foreach (string line in result.Output.Concat(result.Errors))
		{
			Console.Out.WriteLine(line);
			wroteAnything = true;
		}

		if (!wroteAnything)
		{
			AnsiConsole.MarkupLine("[dim](no output)[/]");
		}
	}

	private static void WriteRunSummary(int repoCount, int failCount)
	{
		AnsiConsole.Write(new Rule().LeftJustified());

		string color = failCount > 0 ? "yellow" : "green";
		AnsiConsole.MarkupLine($"[{color}]{repoCount} repos · {repoCount - failCount} ok · {failCount} failed[/]");
	}

	/// <summary>
	/// Updates NuGet packages in all projects under the given path.
	/// </summary>
	public async Task<int> UpdatePackagesAsync(AbsoluteDirectoryPath path, bool includePrerelease = false, CancellationToken ct = default)
	{
		Ensure.NotNull(path);

		string fullPath = path.ToString();
		List<string> solutionFiles = DiscoverSolutionFiles(fullPath);

		if (solutionFiles.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No solution files found.[/]");
			return 0;
		}

		AnsiConsole.MarkupLine($"[blue]Updating packages in {solutionFiles.Count} solution(s)...[/]");

		int updatedCount = 0;

		await AnsiConsole.Progress()
			.AutoClear(false)
			.HideCompleted(false)
			.StartAsync(async progressContext =>
			{
				ProgressTask task = progressContext.AddTask("[green]Updating packages[/]", maxValue: solutionFiles.Count);

				foreach (string sln in solutionFiles)
				{
					ct.ThrowIfCancellationRequested();

					string slnDir = Path.GetDirectoryName(sln) ?? fullPath;
					string slnName = Path.GetFileNameWithoutExtension(sln);

					task.Description = $"[green]Updating {slnName.EscapeMarkup()}[/]";

					updatedCount += await UpdateSolutionPackagesAsync(slnDir, slnName, includePrerelease, ct).ConfigureAwait(false);
					task.Increment(1);
				}
			}).ConfigureAwait(false);

		AnsiConsole.MarkupLine($"[green]Done. Processed {solutionFiles.Count} solution(s).[/]");
		return 0;
	}

	private async Task<int> UpdateSolutionPackagesAsync(string slnDir, string slnName, bool includePrerelease, CancellationToken ct)
	{
		string[] projectFiles = Directory.GetFiles(slnDir, "*.csproj", SearchOption.AllDirectories);
		int updatedCount = 0;

		foreach (string proj in projectFiles)
		{
			updatedCount += await UpdateProjectOutdatedPackagesAsync(proj, slnDir, includePrerelease, ct).ConfigureAwait(false);
			await processService.RunAsync(DotnetCommand, $"restore \"{proj}\"", slnDir, ct).ConfigureAwait(false);
		}

		AnsiConsole.MarkupLine($"  [green]OK[/] {slnName.EscapeMarkup()}");
		return updatedCount;
	}

	private async Task<int> UpdateProjectOutdatedPackagesAsync(string projectFile, string workingDir, bool includePrerelease, CancellationToken ct)
	{
		string prereleaseArg = includePrerelease ? " --prerelease" : string.Empty;
		string outdatedArgs = $"list \"{projectFile}\" package --outdated --format json{prereleaseArg}";

		ProcessResult outdatedResult = await processService.RunAsync(DotnetCommand, outdatedArgs, workingDir, ct).ConfigureAwait(false);

		if (outdatedResult.ExitCode != 0)
		{
			return 0;
		}

		return outdatedResult.Output.Count(line =>
			line.Contains("resolvedVersion", StringComparison.OrdinalIgnoreCase) &&
			line.Contains("latestVersion", StringComparison.OrdinalIgnoreCase));
	}

	private static void DiscoverGitReposRecursive(string directory, ConcurrentBag<string> repos)
	{
		try
		{
			string gitDir = Path.Combine(directory, ".git");

			// Worktrees and submodules store .git as a file holding a gitdir: pointer, so checking
			// only for a directory would silently skip them.
			if (Directory.Exists(gitDir) || File.Exists(gitDir))
			{
				repos.Add(directory);
				return; // Don't recurse into git repos
			}

			foreach (string subDir in Directory.GetDirectories(directory))
			{
				DiscoverGitReposRecursive(subDir, repos);
			}
		}
		catch (UnauthorizedAccessException)
		{
			// Skip inaccessible directories
		}
		catch (DirectoryNotFoundException)
		{
			// Skip deleted directories
		}
	}

	private static List<string> DiscoverSolutionFiles(string directory)
	{
		try
		{
			string[] allSlnFiles = Directory.GetFiles(directory, "*.sln", SearchOption.AllDirectories);

			// Filter out nested solutions (solutions in subdirectories of other solutions)
			List<string> topLevelSlns = [];

			foreach (string sln in allSlnFiles)
			{
				string? slnDir = Path.GetDirectoryName(sln);
				if (slnDir is null)
				{
					continue;
				}

				bool isNested = allSlnFiles.Any(otherSln =>
				{
					string? otherDir = Path.GetDirectoryName(otherSln);
					return otherDir is not null &&
						   !string.Equals(otherDir, slnDir, StringComparison.OrdinalIgnoreCase) &&
						   slnDir.StartsWith(otherDir, StringComparison.OrdinalIgnoreCase);
				});

				if (!isNested)
				{
					topLevelSlns.Add(sln);
				}
			}

			return [.. topLevelSlns.OrderBy(s => Path.GetFileName(s), StringComparer.OrdinalIgnoreCase)];
		}
		catch (UnauthorizedAccessException)
		{
			return [];
		}
	}

	/// <summary>
	/// Walks the filesystem for repositories and solutions and rewrites the cache with what it finds.
	/// </summary>
	private async Task<List<string>> WalkAndCacheAsync(string fullPath, CancellationToken ct)
	{
		ConcurrentBag<string> repos = [];
		await Task.Run(() => DiscoverGitReposRecursive(fullPath, repos), ct).ConfigureAwait(false);

		List<string> sortedRepos = [.. repos.OrderBy(r => Path.GetFileName(r), StringComparer.OrdinalIgnoreCase)];
		List<string> solutionFiles = DiscoverSolutionFiles(fullPath);
		await SaveCacheAsync(sortedRepos, solutionFiles).ConfigureAwait(false);

		return sortedRepos;
	}

	private async Task SaveCacheAsync(IReadOnlyList<string> repositories, IReadOnlyList<string> solutions)
	{
		RepoCacheSettings cache = GetCacheStore();
		cache.Repositories.Clear();
		cache.Solutions.Clear();

		foreach (string repository in repositories.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			cache.Repositories.Add(repository);
		}

		foreach (string solution in solutions.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			cache.Solutions.Add(solution);
		}

		await settingsService.SaveAsync(cache).ConfigureAwait(false);
	}

	private RepoCacheSettings GetCacheStore() => cacheStore ??= settingsService.LoadOrCreate<RepoCacheSettings>();
}
