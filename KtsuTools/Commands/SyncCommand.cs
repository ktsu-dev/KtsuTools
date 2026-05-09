// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.Sync;
using Spectre.Console;
using Spectre.Console.Cli;

/// <summary>
/// Command that synchronizes file contents across repositories.
/// </summary>
public sealed class SyncCommand(SyncService syncService) : AsyncCommand<SyncCommand.Settings>
{
	private readonly SyncService syncService = syncService;

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
	}

	/// <inheritdoc/>
	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);

		string path = string.IsNullOrWhiteSpace(settings.Path)
			? await AnsiConsole.AskAsync<string>("[bold]Root path to scan:[/]").ConfigureAwait(false)
			: settings.Path;

		List<string> filenames = ExpandFilenames(settings.Filename);
		if (filenames.Count == 0)
		{
			string entered = await AnsiConsole.AskAsync<string>("[bold]Filename pattern(s) to scan for (comma-separated):[/]").ConfigureAwait(false);
			filenames = ExpandFilenames([entered]);
		}

		using CtrlCScope scope = new();
		AbsoluteDirectoryPath rootPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(path));
		return await syncService.RunAsync(rootPath, filenames, settings.AutoPush, scope.Token).ConfigureAwait(false);
	}

	private static List<string> ExpandFilenames(IEnumerable<string> raw) =>
		[.. raw
			.Where(v => v is not null)
			.SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			.Where(v => !string.IsNullOrWhiteSpace(v))
			.Distinct(StringComparer.Ordinal)];
}
