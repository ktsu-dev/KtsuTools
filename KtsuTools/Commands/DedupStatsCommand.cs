// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using System.Linq;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.FileDedupe;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class DedupStatsCommand(FileDedupeService dedupeService) : AsyncCommand<DedupStatsCommand.Settings>
{
	private readonly FileDedupeService dedupeService = dedupeService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("-p|--path <PATH>")]
		[Description("Directory to summarize")]
		public required string Path { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();

		AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.Path));
		AnsiConsole.MarkupLine($"[bold]Dedup stats[/] - {path.ToString().EscapeMarkup()}");

		int filesScanned = Directory.Exists(path.ToString())
			? Directory.GetFiles(path.ToString(), "*", SearchOption.AllDirectories).Length
			: 0;

		DedupePlan plan = await dedupeService.PlanAsync(path, scope.Token).ConfigureAwait(false);
		DedupeStats stats = dedupeService.Summarize(plan, filesScanned);

		Table table = new();
		table.AddColumn("Metric");
		table.AddColumn("Value");
		table.AddRow("Files scanned", stats.FilesScanned.ToString());
		table.AddRow("Duplicate groups", stats.DuplicateGroups.ToString());
		table.AddRow("Redundant files", stats.RedundantFiles.ToString());
		table.AddRow("Wasted bytes", stats.WastedBytes.ToString());
		AnsiConsole.Write(table);

		if (stats.CountByExtension.Count > 0)
		{
			AnsiConsole.MarkupLine("[bold]Duplicate files by extension:[/]");
			foreach ((string ext, int count) in stats.CountByExtension.OrderByDescending(kv => kv.Value))
			{
				AnsiConsole.MarkupLine($"  {ext.EscapeMarkup()}: {count}");
			}
		}

		return 0;
	}
}
