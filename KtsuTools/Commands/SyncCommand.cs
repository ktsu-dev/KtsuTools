// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Sync;
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
		public required string Path { get; init; }

		/// <summary>
		/// Gets the filename to scan for.
		/// </summary>
		[CommandOption("--filename <FILENAME>")]
		[Description("The filename to scan for")]
		public required string Filename { get; init; }
	}

	/// <inheritdoc/>
	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		using CancellationTokenSource cts = new();
		return await SyncService.RunAsync(settings.Path, settings.Filename, cts.Token).ConfigureAwait(false);
	}
}
