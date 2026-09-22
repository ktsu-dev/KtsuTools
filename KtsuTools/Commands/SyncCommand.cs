// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Core.UI;
using KtsuTools.Sync;
using Spectre.Console;
using Spectre.Console.Cli;

/// <summary>
/// Command that synchronizes file contents across repositories.
/// </summary>
public sealed class SyncCommand(SyncService syncService, SyncConfigService configService) : AsyncCommand<SyncCommand.Settings>
{
	private readonly SyncService syncService = syncService;
	private readonly SyncConfigService configService = configService;

	/// <summary>
	/// Settings for the sync command.
	/// </summary>
	public sealed class Settings : CommandSettings
	{
		/// <summary>
		/// Gets the root path to recursively scan for files.
		/// </summary>
		[CommandOption("--path <PATH>")]
		[Description("The root path to recursively scan")]
		public string Path { get; init; } = string.Empty;

		/// <summary>
		/// Gets explicit repository paths to sync, as an alternative to walking a single workspace.
		/// </summary>
		[CommandOption("--repo <PATH>")]
		[Description("Path of a repository to sync. Repeat the flag or pass a comma-separated list to name several, and combine it with --path to add them to a workspace scan.")]
#pragma warning disable CA1819 // Properties should not return arrays - Spectre.Console.Cli binds multi-value options via T[] only.
		public string[] Repo { get; init; } = [];
#pragma warning restore CA1819

		/// <summary>
		/// Gets the path of a file listing repository paths, one per line.
		/// </summary>
		[CommandOption("--repo-list <FILE>")]
		[Description("File listing repository paths, one per line, ignoring blank lines and # comments. A relative entry resolves against the file's own directory.")]
		public string RepoList { get; init; } = string.Empty;

		/// <summary>
		/// Gets the directories to leave out of the scan.
		/// </summary>
		[CommandOption("--exclude <DIR>")]
		[Description("Directory to leave out of the scan. A bare name excludes every directory so named; a path excludes that one directory. Repeat the flag or pass a comma-separated list.")]
#pragma warning disable CA1819 // Properties should not return arrays - Spectre.Console.Cli binds multi-value options via T[] only.
		public string[] Exclude { get; init; } = [];
#pragma warning restore CA1819

		/// <summary>
		/// Gets the filename patterns to scan for. May be specified multiple times or comma-separated.
		/// </summary>
		[CommandOption("--filename <FILENAME>")]
		[Description("Filename pattern to scan for. Repeat the flag or pass a comma-separated list to sync several files in one run.")]
#pragma warning disable CA1819 // Properties should not return arrays - Spectre.Console.Cli binds multi-value options via T[] only.
		public string[] Filename { get; init; } = [];
#pragma warning restore CA1819

		/// <summary>
		/// Gets a value indicating whether to push without prompting when all unpushed commits were authored by KtsuTools.
		/// </summary>
		[CommandOption("--auto-push")]
		[Description("Push without prompting when every unpushed commit on a repo was authored by KtsuTools.")]
		public bool AutoPush { get; init; }

		/// <summary>
		/// Gets the branch to commit onto in each repo instead of whatever is checked out.
		/// </summary>
		[CommandOption("--branch <NAME>")]
		[Description("Commit onto a branch of this name in each repo, created if missing and reused if it already exists, restoring the original branch afterwards.")]
		public string Branch { get; init; } = string.Empty;

		/// <summary>
		/// Gets a value indicating whether to open a pull request in each repo whose sync branch was pushed.
		/// </summary>
		[CommandOption("--pr")]
		[Description("Open a pull request in each repo whose sync branch was pushed, using the gh CLI when it is installed and the GitHub API otherwise. Requires --branch.")]
		public bool OpenPullRequest { get; init; }

		/// <summary>
		/// Gets the name of a saved configuration supplying the path, filenames and options.
		/// </summary>
		[CommandOption("--config <NAME>")]
		[Description("Run a saved configuration by name (use 'sync-config save' to create one). Explicit flags override its values.")]
		public string ConfigName { get; init; } = string.Empty;

		/// <inheritdoc/>
		public override ValidationResult Validate() =>
			// A saved configuration can supply the branch, and it is not loaded until the run starts, so only a run
			// that names no configuration can be rejected here.
			OpenPullRequest && string.IsNullOrWhiteSpace(Branch) && string.IsNullOrWhiteSpace(ConfigName)
				? ValidationResult.Error("--pr requires --branch: there is nothing to open a pull request from when sync commits onto the checked-out branch.")
				: ValidationResult.Success();
	}

	/// <inheritdoc/>
	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);

		SyncConfigEntry? saved = null;
		if (!string.IsNullOrWhiteSpace(settings.ConfigName))
		{
			saved = configService.Get(settings.ConfigName);
			if (saved is null)
			{
				AnsiConsole.MarkupLine($"[red]Error: no sync configuration named '{settings.ConfigName.EscapeMarkup()}'. Run 'sync-config list' to see saved configurations.[/]");
				return 1;
			}

			AnsiConsole.MarkupLine($"[dim]Running sync configuration '{settings.ConfigName.EscapeMarkup()}'.[/]");
		}

		SyncRunOptions options = SyncConfigResolver.Resolve(
			saved,
			settings.Path,
			settings.Filename,
			settings.AutoPush,
			settings.Branch,
			settings.OpenPullRequest);

		if (options.OpenPullRequest && string.IsNullOrWhiteSpace(options.Branch))
		{
			AnsiConsole.MarkupLine("[red]Error: --pr requires a branch, and neither the flags nor the saved configuration named one.[/]");
			return 1;
		}

		// A saved configuration supplies only --path; --repo, --repo-list and --exclude stay flag-only for now.
		IReadOnlyList<string> roots = SyncService.ResolveRoots(options.Path, settings.Repo, settings.RepoList);
		if (roots.Count == 0)
		{
			// Only ask when nothing at all was named; --repo and --repo-list already answer the question.
			string enteredRoot = await AnsiConsole.AskAsync<string>("[bold]Root path to scan:[/]", cancellationToken).ConfigureAwait(false);
			roots = SyncService.ResolveRoots(enteredRoot, repos: null, repoListFile: null);
		}

		Collection<string> filenames = options.Filenames;
		if (filenames.Count == 0)
		{
			string entered = await AnsiConsole.AskAsync<string>("[bold]Filename pattern(s) to scan for (comma-separated):[/]", cancellationToken).ConfigureAwait(false);
			filenames = SyncConfigResolver.ExpandFilenames([entered]);
		}

		using CtrlCScope scope = new();
		return await syncService
			.RunAsync(roots, filenames, options.AutoPush, options.Branch, settings.Exclude, options.OpenPullRequest, scope.Token)
			.ConfigureAwait(false);
	}
}
