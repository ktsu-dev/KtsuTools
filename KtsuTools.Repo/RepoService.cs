// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Repo;

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using KtsuTools.Core.Services.Git;
using KtsuTools.Core.Services.Process;
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
public class RepoService(IGitService gitService, IProcessService processService)
{
	private readonly IGitService gitService = gitService;
	private readonly IProcessService processService = processService;

	/// <summary>
	/// Discovers git repositories in the given directory.
	/// </summary>
	public async Task<IReadOnlyList<string>> DiscoverRepositoriesAsync(string path, CancellationToken ct = default)
	{
		_ = gitService;
		Ensure.NotNull(path);

		string fullPath = Path.GetFullPath(path);

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
	/// Builds and tests all solutions found in repositories under the given path.
	/// </summary>
	public async Task<int> BuildAndTestAsync(string path, bool parallel = false, CancellationToken ct = default)
	{
		_ = parallel;
		Ensure.NotNull(path);

		string fullPath = Path.GetFullPath(path);
		List<string> solutionFiles = DiscoverSolutionFiles(fullPath);

		if (solutionFiles.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No solution files found.[/]");
			return 0;
		}

		AnsiConsole.MarkupLine($"[blue]Found {solutionFiles.Count} solution(s).[/]");

		int failCount = 0;

		await AnsiConsole.Progress()
			.AutoClear(false)
			.HideCompleted(false)
			.StartAsync(async progressContext =>
			{
				ProgressTask task = progressContext.AddTask("[green]Building and testing[/]", maxValue: solutionFiles.Count);

				foreach (string sln in solutionFiles)
				{
					ct.ThrowIfCancellationRequested();

					string slnDir = Path.GetDirectoryName(sln) ?? fullPath;
					string slnName = Path.GetFileNameWithoutExtension(sln);

					task.Description = $"[green]Building {slnName.EscapeMarkup()}[/]";

					// Build
					ProcessResult buildResult = await processService.RunAsync("dotnet", "build --nologo -v q", slnDir, ct).ConfigureAwait(false);

					if (buildResult.ExitCode != 0)
					{
						AnsiConsole.MarkupLine($"  [red]FAIL[/] {slnName.EscapeMarkup()} - build failed");
						failCount++;
						task.Increment(1);
						continue;
					}

					// Test
					ProcessResult testResult = await processService.RunAsync("dotnet", "test --nologo --no-build -v q", slnDir, ct).ConfigureAwait(false);

					if (testResult.ExitCode != 0)
					{
						AnsiConsole.MarkupLine($"  [yellow]WARN[/] {slnName.EscapeMarkup()} - build OK, tests failed");
						failCount++;
					}
					else
					{
						AnsiConsole.MarkupLine($"  [green]OK[/]   {slnName.EscapeMarkup()}");
					}

					task.Increment(1);
				}
			}).ConfigureAwait(false);

		AnsiConsole.MarkupLine(failCount > 0
			? $"[yellow]Done. {failCount} solution(s) had failures.[/]"
			: "[green]Done. All solutions built and tested successfully.[/]");

		return failCount > 0 ? 1 : 0;
	}

	/// <summary>
	/// Pulls all repositories under the given path.
	/// </summary>
	public async Task<int> PullAllAsync(string path, CancellationToken ct = default)
	{
		_ = gitService;
		Ensure.NotNull(path);

		string fullPath = Path.GetFullPath(path);
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

					ProcessResult result = await processService.RunAsync("git", "pull --all --autostash", repo, ct).ConfigureAwait(false);

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
	/// Updates NuGet packages in all projects under the given path.
	/// </summary>
	public async Task<int> UpdatePackagesAsync(string path, bool includePrerelease = false, CancellationToken ct = default)
	{
		Ensure.NotNull(path);

		string fullPath = Path.GetFullPath(path);
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

					// Find project files
					string[] projectFiles = Directory.GetFiles(slnDir, "*.csproj", SearchOption.AllDirectories);

					foreach (string proj in projectFiles)
					{
						string prereleaseArg = includePrerelease ? " --prerelease" : string.Empty;
						string outdatedArgs = $"list \"{proj}\" package --outdated --format json{prereleaseArg}";

						ProcessResult outdatedResult = await processService.RunAsync("dotnet", outdatedArgs, slnDir, ct).ConfigureAwait(false);

						if (outdatedResult.ExitCode == 0)
						{
							// Parse outdated packages from output and update
							foreach (string line in outdatedResult.Output)
							{
								if (line.Contains("resolvedVersion", StringComparison.OrdinalIgnoreCase) &&
									line.Contains("latestVersion", StringComparison.OrdinalIgnoreCase))
								{
									updatedCount++;
								}
							}
						}

						string updateArgs = $"add \"{proj}\" package --no-restore{prereleaseArg}";
						await processService.RunAsync("dotnet", $"restore \"{proj}\"", slnDir, ct).ConfigureAwait(false);
					}

					AnsiConsole.MarkupLine($"  [green]OK[/] {slnName.EscapeMarkup()}");
					task.Increment(1);
				}
			}).ConfigureAwait(false);

		AnsiConsole.MarkupLine($"[green]Done. Processed {solutionFiles.Count} solution(s).[/]");
		return 0;
	}

	private static void DiscoverGitReposRecursive(string directory, ConcurrentBag<string> repos)
	{
		try
		{
			string gitDir = Path.Combine(directory, ".git");
			if (Directory.Exists(gitDir))
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
}
