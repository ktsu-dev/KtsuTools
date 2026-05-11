// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.FileDedupe;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class DedupDeleteCommand(FileDedupeService dedupeService) : AsyncCommand<DedupDeleteCommand.Settings>
{
	private readonly FileDedupeService dedupeService = dedupeService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("-p|--path <PATH>")]
		[Description("Directory to scan for duplicates")]
		public required string Path { get; init; }

		[CommandOption("-y|--yes")]
		[Description("Skip confirmation prompt")]
		[DefaultValue(false)]
		public bool AssumeYes { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();

		AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.Path));
		AnsiConsole.MarkupLine($"[bold]Dedup delete[/] - {path.ToString().EscapeMarkup()}");

		DedupePlan plan = await dedupeService.PlanAsync(path, scope.Token).ConfigureAwait(false);

		if (plan.Removals.Count == 0)
		{
			AnsiConsole.MarkupLine("[green]Nothing to delete.[/]");
			return 0;
		}

		AnsiConsole.MarkupLine($"[yellow]Will delete {plan.Removals.Count} file(s); shortest-filename winners are kept.[/]");

		if (!settings.AssumeYes)
		{
			bool ok = AnsiConsole.Confirm("Proceed with deletion?", defaultValue: false);
			if (!ok)
			{
				AnsiConsole.MarkupLine("[dim]Cancelled.[/]");
				return 0;
			}
		}

		int deleted = dedupeService.DeleteRedundant(plan);
		AnsiConsole.MarkupLine($"[green]Deleted {deleted} file(s).[/]");
		return 0;
	}
}
