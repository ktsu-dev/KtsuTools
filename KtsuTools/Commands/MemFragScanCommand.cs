// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class MemFragScanCommand : AsyncCommand<MemFragScanCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--pid <PID>")]
		[Description("Process ID to scan for memory fragmentation")]
		public required int ProcessId { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		AnsiConsole.MarkupLine("[bold]MemFrag Scan[/]");
		AnsiConsole.MarkupLine("[yellow]Not yet implemented.[/]");
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
