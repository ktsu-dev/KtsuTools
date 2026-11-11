// Copyright (c) 2023-2026 ktsu-dev contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ktsu.KtsuTools.Test")]
// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.SvnMigrate;

using System.Diagnostics.CodeAnalysis;
using ktsu.Semantics.Paths;
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
	public async Task<int> MigrateAsync(Uri svnUrl, AbsoluteDirectoryPath targetPath, AbsoluteFilePath? authorsFile = null, bool preserveEmptyDirs = true, CancellationToken ct = default)
	{
		Ensure.NotNull(svnUrl);
		Ensure.NotNull(targetPath);

		string gitRepoPath = targetPath.ToString();
		string? authorsFilePath = authorsFile?.ToString();

		// Validate prerequisites
		List<string> errors = await ValidateAsync(gitRepoPath, authorsFilePath, ct).ConfigureAwait(false);
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
					new TaskDescriptionColumn(),
					new ProgressBarColumn(),
					new PercentageColumn(),
					new SpinnerColumn())
				.StartAsync(async progressContext =>
				{
					ProgressTask task = progressContext.AddTask("[green]Migrating SVN to Git[/]", maxValue: 100);

					// Phase 1: Clone SVN repository
					task.Description = "[green]Cloning SVN repository with git-svn...[/]";
					await CloneSvnRepositoryAsync(svnUrl.AbsoluteUri, gitRepoPath, authorsFilePath, preserveEmptyDirs, ct).ConfigureAwait(false);
					task.Value = 50;

					// Phase 2: Cleanup references
					task.Description = "[green]Converting git-svn references to local branches and tags...[/]";
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

	private async Task<List<string>> ValidateAsync(string gitRepoPath, string? authorsFile, CancellationToken ct)
	{
		List<string> errors = [];

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

		ProcessResult result = await processService.RunAsync("git", args, null, ct).ConfigureAwait(false);

		if (result.ExitCode != 0)
		{
			string errorMessage = string.Join(Environment.NewLine, result.Errors);
			throw new InvalidOperationException($"git svn clone failed: {errorMessage}");
		}

		// Every later step runs inside the clone, so a clone that reported success but left nothing behind
		// would otherwise surface as a process that cannot start in a missing directory.
		if (!Directory.Exists(gitRepoPath))
		{
			throw new InvalidOperationException($"git svn clone did not create {gitRepoPath}");
		}
	}

	private async Task CleanupGitSvnReferencesAsync(string gitRepoPath, CancellationToken ct)
	{
		ProcessResult branchResult = await processService.RunAsync("git", "branch -r", gitRepoPath, ct).ConfigureAwait(false);

		if (branchResult.ExitCode != 0)
		{
			throw new InvalidOperationException($"git branch -r failed: {string.Join(Environment.NewLine, branchResult.Errors)}");
		}

		List<string> failures = [];

		foreach (string line in branchResult.Output)
		{
			// git-svn --stdlayout names the SVN layout under origin/: trunk is origin/trunk, a branch is
			// origin/<branch>, and a tag is origin/tags/<tag>. Only refs in that layout are converted.
			string remoteRef = line.Trim();
			if (!TryClassifyRemoteRef(remoteRef, out string? localName, out bool isTag))
			{
				continue;
			}

			// A tag becomes a git tag rather than a tags/<name> branch, so the migrated repository
			// carries its releases as tags once it is pushed.
			string[] arguments = isTag
				? ["tag", localName, remoteRef]
				: ["checkout", "-b", localName, remoteRef];

			ProcessResult result = await processService.RunAsync("git", arguments, gitRepoPath, ct).ConfigureAwait(false);
			if (result.ExitCode != 0)
			{
				failures.Add($"git {string.Join(' ', arguments)}: {string.Join(' ', result.Errors)}");
			}
		}

		ProcessResult checkout = await processService.RunAsync("git", "checkout master", gitRepoPath, ct).ConfigureAwait(false);
		if (checkout.ExitCode != 0)
		{
			failures.Add($"git checkout master: {string.Join(' ', checkout.Errors)}");
		}

		// Every ref is attempted before failing, so one bad name does not hide the others; a branch
		// or tag that could not be created would otherwise be missing from the migration unnoticed.
		if (failures.Count > 0)
		{
			throw new InvalidOperationException(
				$"could not convert {failures.Count} git-svn reference(s):{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
		}
	}

	/// <summary>
	/// Works out what a git-svn remote ref becomes in the migrated repository.
	/// </summary>
	/// <param name="remoteRef">A ref as <c>git branch -r</c> lists it, such as <c>origin/tags/v1.0</c>.</param>
	/// <param name="localName">The local branch or tag name to create.</param>
	/// <param name="isTag">Whether the ref is an SVN tag, to be created as a git tag.</param>
	/// <returns>False for a ref that is not converted: trunk, which master already tracks, and git-svn's own refs.</returns>
	internal static bool TryClassifyRemoteRef(string remoteRef, [NotNullWhen(true)] out string? localName, out bool isTag)
	{
		const string RemotePrefix = "origin/";
		const string TagPrefix = "tags/";

		localName = null;
		isTag = false;

		if (string.IsNullOrEmpty(remoteRef) ||
			!remoteRef.StartsWith(RemotePrefix, StringComparison.Ordinal) ||
			remoteRef.Contains("git-svn", StringComparison.Ordinal))
		{
			return false;
		}

		string name = remoteRef[RemotePrefix.Length..];

		// Only trunk itself is skipped. A branch whose name merely contains "trunk", such as
		// trunk-2.0 or merge-trunk-fixes, is a branch like any other.
		if (name.Length == 0 || name == "trunk")
		{
			return false;
		}

		if (name.StartsWith(TagPrefix, StringComparison.Ordinal))
		{
			name = name[TagPrefix.Length..];
			if (name.Length == 0)
			{
				return false;
			}

			isTag = true;
		}

		localName = name;
		return true;
	}

	private async Task FinalizeRepositoryAsync(string gitRepoPath, CancellationToken ct) =>
		await processService.RunAsync("git", "gc --aggressive", gitRepoPath, ct).ConfigureAwait(false);
}
