// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.Collections.Generic;
using System.Threading;
using KtsuTools.Merge;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class MergeBatchListCommand(MergeBatchService batchService) : Command<MergeBatchListCommand.Settings>
{
	private readonly MergeBatchService batchService = batchService;

	public sealed class Settings : CommandSettings
	{
	}

	protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		IReadOnlyDictionary<string, MergeBatchEntry> batches = batchService.List();

		if (batches.Count == 0)
		{
			AnsiConsole.MarkupLine("[dim]No saved batches. Use 'merge-batch save <name> <directory> <filename>' to create one.[/]");
			return 0;
		}

		Table table = new();
		table.AddColumn("Name");
		table.AddColumn("Directory");
		table.AddColumn("Filename");
		table.AddColumn("Diff Style");

		foreach (KeyValuePair<string, MergeBatchEntry> kvp in batches)
		{
			table.AddRow(
				kvp.Key.EscapeMarkup(),
				kvp.Value.Directory.EscapeMarkup(),
				kvp.Value.Filename.EscapeMarkup(),
				(kvp.Value.DiffStyle ?? "(default)").EscapeMarkup());
		}

		AnsiConsole.Write(table);
		return 0;
	}
}
