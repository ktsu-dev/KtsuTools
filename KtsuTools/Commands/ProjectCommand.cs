// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class ProjectCommand : AsyncCommand<ProjectCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--owner <OWNER>")]
		[Description("GitHub owner or organization")]
		[DefaultValue("ktsu-dev")]
		public string Owner { get; init; } = "ktsu-dev";
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		AnsiConsole.MarkupLine("[bold]Project[/]");
		AnsiConsole.MarkupLine("[yellow]Not yet implemented.[/]");
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
