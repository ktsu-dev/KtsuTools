// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.Collections.Generic;
using System.Threading;
using KtsuTools.Sync;
using Spectre.Console;
using Spectre.Console.Cli;

/// <summary>
/// Command that lists every saved sync configuration.
/// </summary>
public sealed class SyncConfigListCommand(SyncConfigService configService) : Command<SyncConfigListCommand.Settings>
{
	private readonly SyncConfigService configService = configService;

	/// <summary>
	/// Settings for the sync-config list command.
	/// </summary>
	public sealed class Settings : CommandSettings
	{
	}

	/// <inheritdoc/>
	protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		IReadOnlyDictionary<string, SyncConfigEntry> configs = configService.List();

		if (configs.Count == 0)
		{
			AnsiConsole.MarkupLine("[dim]No saved sync configurations. Use 'sync-config save <name> <path> --filename <FILENAME>' to create one.[/]");
			return 0;
		}

		Table table = new();
		table.AddColumn("Name");
		table.AddColumn("Path");
		table.AddColumn("Filenames");
		table.AddColumn("Branch");
		table.AddColumn("Auto Push");
		table.AddColumn("PR");

		foreach (KeyValuePair<string, SyncConfigEntry> kvp in configs)
		{
			table.AddRow(
				kvp.Key.EscapeMarkup(),
				kvp.Value.Path.EscapeMarkup(),
				string.Join(", ", kvp.Value.Filenames).EscapeMarkup(),
				(kvp.Value.Branch ?? "(checked out)").EscapeMarkup(),
				kvp.Value.AutoPush ? "yes" : "no",
				kvp.Value.OpenPullRequest ? "yes" : "no");
		}

		AnsiConsole.Write(table);
		return 0;
	}
}
