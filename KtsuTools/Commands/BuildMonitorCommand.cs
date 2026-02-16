// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class BuildMonitorCommand : AsyncCommand<BuildMonitorCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--owner <OWNER>")]
		[Description("GitHub owner or organization to monitor")]
		public required string Owner { get; init; }

		[CommandOption("--refresh <SECONDS>")]
		[Description("Refresh interval in seconds")]
		[DefaultValue(30)]
		public int RefreshInterval { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		AnsiConsole.MarkupLine("[bold]Build Monitor[/]");
		AnsiConsole.MarkupLine("[yellow]Not yet implemented.[/]");
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
