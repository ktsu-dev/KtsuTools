// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using KtsuTools.Core.UI;
using KtsuTools.Merge;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class MergeHistoryCommand(MergeHistoryService historyService) : AsyncCommand<MergeHistoryCommand.Settings>
{
	private readonly MergeHistoryService historyService = historyService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--clear")]
		[Description("Truncate the merge history instead of listing it")]
		[DefaultValue(false)]
		public bool Clear { get; init; }
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();

		if (settings.Clear)
		{
			await historyService.ClearAsync(scope.Token).ConfigureAwait(false);
			AnsiConsole.MarkupLine("[green]Merge history cleared.[/]");
			return 0;
		}

		IReadOnlyList<MergeHistoryEntry> entries = historyService.List();
		if (entries.Count == 0)
		{
			AnsiConsole.MarkupLine("[dim]No merge runs recorded yet.[/]");
			return 0;
		}

		Table table = new();
		table.AddColumn("When");
		table.AddColumn("Directory");
		table.AddColumn("Filename");
		table.AddColumn("Diff");
		table.AddColumn("Batch");
		table.AddColumn("Exit");
		table.Border = TableBorder.Rounded;

		foreach (MergeHistoryEntry entry in entries)
		{
			string exit = entry.ExitCode == 0
				? $"[green]{entry.ExitCode}[/]"
				: $"[red]{entry.ExitCode}[/]";

			table.AddRow(
				entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm").EscapeMarkup(),
				entry.Directory.EscapeMarkup(),
				entry.Filename.EscapeMarkup(),
				entry.DiffStyle.EscapeMarkup(),
				(entry.BatchName ?? "-").EscapeMarkup(),
				exit);
		}

		AnsiConsole.Write(table);
		return 0;
	}
}
