// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.FileDedupe;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class DedupDryRunCommand(FileDedupeService dedupeService) : AsyncCommand<DedupDryRunCommand.Settings>
{
	private readonly FileDedupeService dedupeService = dedupeService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("-p|--path <PATH>")]
		[Description("Directory to scan for duplicates")]
		public required string Path { get; init; }
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();

		AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.Path));
		AnsiConsole.MarkupLine($"[bold]Dedup dry-run[/] - {path.ToString().EscapeMarkup()}");

		DedupePlan plan = await dedupeService.PlanAsync(path, scope.Token).ConfigureAwait(false);

		if (plan.Removals.Count == 0)
		{
			AnsiConsole.MarkupLine("[green]Nothing to delete.[/]");
			return 0;
		}

		AnsiConsole.MarkupLine("[yellow]Would delete (shortest-filename winners are kept):[/]");
		foreach (string keeper in plan.Keepers)
		{
			AnsiConsole.MarkupLine($"  [green]keep[/] {keeper.EscapeMarkup()}");
		}

		foreach (string removal in plan.Removals)
		{
			AnsiConsole.MarkupLine($"  [red]drop[/] {removal.EscapeMarkup()}");
		}

		AnsiConsole.MarkupLine($"[blue]{plan.Removals.Count} file(s), {plan.WastedBytes} byte(s) would be reclaimed.[/]");
		return 0;
	}
}
