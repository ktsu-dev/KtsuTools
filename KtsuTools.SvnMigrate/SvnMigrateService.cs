// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.SvnMigrate;

using KtsuTools.Core.Services.Process;
using Spectre.Console;

/// <summary>
/// Service for migrating SVN repositories to Git using git-svn.
/// </summary>
public class SvnMigrateService(IProcessService processService)
{
	private readonly IProcessService processService = processService;

	/// <summary>
	/// Migrates an SVN repository to Git.
	/// </summary>
	public async Task<int> MigrateAsync(Uri svnUrl, string targetPath, string? authorsFile = null, bool preserveEmptyDirs = true, CancellationToken ct = default)
	{
		Ensure.NotNull(svnUrl);
		Ensure.NotNull(targetPath);

		string gitRepoPath = Path.GetFullPath(targetPath);

		// Validate prerequisites
		List<string> errors = await ValidateAsync(svnUrl, gitRepoPath, authorsFile, ct).ConfigureAwait(false);
		if (errors.Count > 0)
		{
			foreach (string error in errors)
			{
				AnsiConsole.MarkupLine($"[red]{error.EscapeMarkup()}[/]");
			}

			return 1;
		}

		// Perform migration with progress
		try
		{
			await AnsiConsole.Progress()
				.AutoClear(false)
				.HideCompleted(false)
				.Columns(
				[
					new TaskDescriptionColumn(),
					new ProgressBarColumn(),
					new PercentageColumn(),
					new SpinnerColumn(),
				])
				.StartAsync(async progressContext =>
				{
					ProgressTask task = progressContext.AddTask("[green]Migrating SVN to Git[/]", maxValue: 100);

					// Phase 1: Clone SVN repository
					task.Description = "[green]Cloning SVN repository with git-svn...[/]";
					await CloneSvnRepositoryAsync(svnUrl.ToString(), gitRepoPath, authorsFile, preserveEmptyDirs, ct).ConfigureAwait(false);
					task.Value = 50;

					// Phase 2: Cleanup references
					task.Description = "[green]Converting git-svn references to local branches...[/]";
					await CleanupGitSvnReferencesAsync(gitRepoPath, ct).ConfigureAwait(false);
					task.Value = 80;

					// Phase 3: Finalize
					task.Description = "[green]Optimizing repository...[/]";
					await FinalizeRepositoryAsync(gitRepoPath, ct).ConfigureAwait(false);
					task.Value = 100;

					task.Description = "[green]Migration complete![/]";
				}).ConfigureAwait(false);

			AnsiConsole.MarkupLine($"[bold green]Repository successfully migrated to:[/] {gitRepoPath.EscapeMarkup()}");
			return 0;
		}
		catch (OperationCanceledException)
		{
			AnsiConsole.MarkupLine("[yellow]Migration was cancelled.[/]");
			return 1;
		}
		catch (InvalidOperationException ex)
		{
			AnsiConsole.MarkupLine($"[red]Migration failed: {ex.Message.EscapeMarkup()}[/]");
			return 1;
		}
	}

	private async Task<List<string>> ValidateAsync(Uri svnUrl, string gitRepoPath, string? authorsFile, CancellationToken ct)
	{
		List<string> errors = [];

		if (string.IsNullOrWhiteSpace(svnUrl.ToString()))
		{
			errors.Add("SVN repository URL is required.");
		}

		if (string.IsNullOrWhiteSpace(gitRepoPath))
		{
			errors.Add("Git repository path is required.");
		}

		if (!string.IsNullOrWhiteSpace(authorsFile) && !File.Exists(authorsFile))
		{
			errors.Add($"Authors file does not exist: {authorsFile}");
		}

		// Check if git-svn is available
		ProcessResult gitSvnCheck = await processService.RunAsync("git", "svn --version", null, ct).ConfigureAwait(false);
		if (gitSvnCheck.ExitCode != 0)
		{
			errors.Add("git-svn is not available. Please install Git with SVN support.");
		}

		return errors;
	}

	private async Task CloneSvnRepositoryAsync(string svnPath, string gitRepoPath, string? authorsFile, bool preserveEmptyDirs, CancellationToken ct)
	{
		List<string> args = ["svn", "clone", svnPath, gitRepoPath, "--stdlayout"];

		if (!string.IsNullOrWhiteSpace(authorsFile))
		{
			args.Add($"--authors-file={authorsFile}");
		}

		if (preserveEmptyDirs)
		{
			args.Add("--preserve-empty-dirs");
		}

		string arguments = string.Join(" ", args);
		ProcessResult result = await processService.RunAsync("git", arguments, null, ct).ConfigureAwait(false);

		if (result.ExitCode != 0)
		{
			string errorMessage = string.Join(Environment.NewLine, result.Errors);
			throw new InvalidOperationException($"git svn clone failed: {errorMessage}");
		}
	}

	private async Task CleanupGitSvnReferencesAsync(string gitRepoPath, CancellationToken ct)
	{
		ProcessResult branchResult = await processService.RunAsync("git", "branch -r", gitRepoPath, ct).ConfigureAwait(false);

		if (branchResult.ExitCode != 0)
		{
			return;
		}

		foreach (string line in branchResult.Output)
		{
			string branch = line.Trim();
			if (string.IsNullOrEmpty(branch) ||
				branch.Contains("git-svn", StringComparison.Ordinal) ||
				branch.Contains("trunk", StringComparison.Ordinal))
			{
				continue;
			}

			if (branch.StartsWith("origin/", StringComparison.OrdinalIgnoreCase))
			{
				string localName = branch[7..];
				await processService.RunAsync("git", $"checkout -b {localName} {branch}", gitRepoPath, ct).ConfigureAwait(false);
			}
		}

		await processService.RunAsync("git", "checkout master", gitRepoPath, ct).ConfigureAwait(false);
	}

	private async Task FinalizeRepositoryAsync(string gitRepoPath, CancellationToken ct) =>
		await processService.RunAsync("git", "gc --aggressive", gitRepoPath, ct).ConfigureAwait(false);
}
