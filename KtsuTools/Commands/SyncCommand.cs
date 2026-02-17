// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Sync;
using Spectre.Console;
using Spectre.Console.Cli;

/// <summary>
/// Command that synchronizes file contents across repositories.
/// </summary>
public sealed class SyncCommand : AsyncCommand<SyncCommand.Settings>
{
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
		/// Gets the filename to scan for.
		/// </summary>
		[CommandOption("--filename <FILENAME>")]
		[Description("The filename to scan for")]
		public string Filename { get; init; } = string.Empty;
	}

	/// <inheritdoc/>
	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);

		string path = string.IsNullOrWhiteSpace(settings.Path)
			? await AnsiConsole.AskAsync<string>("[bold]Root path to scan:[/]").ConfigureAwait(false)
			: settings.Path;

		string filename = string.IsNullOrWhiteSpace(settings.Filename)
			? await AnsiConsole.AskAsync<string>("[bold]Filename pattern to scan for:[/]").ConfigureAwait(false)
			: settings.Filename;

		using CancellationTokenSource cts = new();
		return await SyncService.RunAsync(path, filename, cts.Token).ConfigureAwait(false);
	}
}
