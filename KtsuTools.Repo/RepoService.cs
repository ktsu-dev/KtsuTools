// Copyright (c) 2023-2026 ktsu-dev contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ktsu.KtsuTools.Test")]
// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Repo;

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
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

		ConcurrentBag<string> repos = [];

		await AnsiConsole.Status()
			.Spinner(Spinner.Known.Star)
			.StartAsync("Discovering repositories...", async ctx =>
			{
				await Task.Run(() => DiscoverGitReposRecursive(fullPath, repos), ct).ConfigureAwait(false);

				ctx.Status($"Found {repos.Count} repositories");
			}).ConfigureAwait(false);

		List<string> sortedRepos = [.. repos.OrderBy(r => Path.GetFileName(r), StringComparer.OrdinalIgnoreCase)];
		List<string> solutionFiles = DiscoverSolutionFiles(fullPath);
		await SaveCacheAsync(sortedRepos, solutionFiles).ConfigureAwait(false);

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
