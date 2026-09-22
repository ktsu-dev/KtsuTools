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
/// Command that saves a named set of sync inputs.
/// </summary>
public sealed class SyncConfigSaveCommand(SyncConfigService configService) : AsyncCommand<SyncConfigSaveCommand.Settings>
{
	private readonly SyncConfigService configService = configService;

	/// <summary>
	/// Settings for the sync-config save command.
	/// </summary>
	public sealed class Settings : CommandSettings
	{
		/// <summary>
		/// Gets the name to save this configuration under.
		/// </summary>
		[CommandArgument(0, "<name>")]
		[Description("Name to save this configuration under")]
		public required string Name { get; init; }

		/// <summary>
		/// Gets the root path to recursively scan for files.
		/// </summary>
		[CommandArgument(1, "<path>")]
		[Description("The root path to recursively scan")]
		public required string Path { get; init; }

		/// <summary>
		/// Gets the filename patterns to scan for. May be specified multiple times or comma-separated.
		/// </summary>
		[CommandOption("--filename <FILENAME>")]
		[Description("Filename pattern to scan for. Repeat the flag or pass a comma-separated list to save several files in one configuration.")]
#pragma warning disable CA1819 // Properties should not return arrays - Spectre.Console.Cli binds multi-value options via T[] only.
		public string[] Filename { get; init; } = [];
#pragma warning restore CA1819

		/// <summary>
		/// Gets a value indicating whether runs of this configuration push without prompting.
		/// </summary>
		[CommandOption("--auto-push")]
		[Description("Save --auto-push with this configuration.")]
		public bool AutoPush { get; init; }

		/// <summary>
		/// Gets the branch runs of this configuration commit onto.
		/// </summary>
		[CommandOption("--branch <NAME>")]
		[Description("Save the branch runs of this configuration commit onto.")]
		public string Branch { get; init; } = string.Empty;

		/// <summary>
		/// Gets a value indicating whether runs of this configuration open pull requests.
		/// </summary>
		[CommandOption("--pr")]
		[Description("Save --pr with this configuration. Requires --branch.")]
		public bool OpenPullRequest { get; init; }

		/// <inheritdoc/>
		public override ValidationResult Validate()
		{
			if (SyncConfigResolver.ExpandFilenames(Filename).Count == 0)
			{
				return ValidationResult.Error("At least one --filename is required: a configuration with no filenames would sync nothing.");
			}

			return OpenPullRequest && string.IsNullOrWhiteSpace(Branch)
				? ValidationResult.Error("--pr requires --branch: there is nothing to open a pull request from when sync commits onto the checked-out branch.")
				: ValidationResult.Success();
		}
	}

	/// <inheritdoc/>
	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();

		Collection<string> filenames = SyncConfigResolver.ExpandFilenames(settings.Filename);

		SyncConfigEntry entry = new()
		{
			Path = settings.Path,
			Filenames = filenames,
			AutoPush = settings.AutoPush,
			Branch = string.IsNullOrWhiteSpace(settings.Branch) ? null : settings.Branch,
			OpenPullRequest = settings.OpenPullRequest,
		};

		await configService.SaveAsync(settings.Name, entry, scope.Token).ConfigureAwait(false);
		AnsiConsole.MarkupLine($"[green]Saved sync configuration '{settings.Name.EscapeMarkup()}'.[/]");
		return 0;
	}
}
