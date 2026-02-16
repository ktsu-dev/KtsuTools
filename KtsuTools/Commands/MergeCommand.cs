// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class MergeCommand : AsyncCommand<MergeCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<directory>")]
		[Description("Directory containing files to merge")]
		public required string Directory { get; init; }

		[CommandArgument(1, "<filename>")]
		[Description("Filename pattern to merge")]
		public required string Filename { get; init; }

		[CommandOption("--batch <NAME>")]
		[Description("Batch name for grouping merge operations")]
		public string? BatchName { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		AnsiConsole.MarkupLine("[bold]Merge[/]");
		AnsiConsole.MarkupLine("[yellow]Not yet implemented.[/]");
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
