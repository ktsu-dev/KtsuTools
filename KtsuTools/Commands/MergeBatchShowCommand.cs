// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using KtsuTools.Merge;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class MergeBatchShowCommand(MergeBatchService batchService) : Command<MergeBatchShowCommand.Settings>
{
	private readonly MergeBatchService batchService = batchService;

	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<name>")]
		[Description("Name of the batch to show")]
		public required string Name { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		MergeBatchEntry? entry = batchService.Get(settings.Name);

		if (entry is null)
		{
			AnsiConsole.MarkupLine($"[red]No batch named '{settings.Name.EscapeMarkup()}'.[/]");
			return 1;
		}

		AnsiConsole.MarkupLine($"[bold]Batch:[/] {settings.Name.EscapeMarkup()}");
		AnsiConsole.MarkupLine($"  [dim]Directory:[/]  {entry.Directory.EscapeMarkup()}");
		AnsiConsole.MarkupLine($"  [dim]Filename:[/]   {entry.Filename.EscapeMarkup()}");
		AnsiConsole.MarkupLine($"  [dim]Diff style:[/] {(entry.DiffStyle ?? "(default)").EscapeMarkup()}");
		return 0;
	}
}
