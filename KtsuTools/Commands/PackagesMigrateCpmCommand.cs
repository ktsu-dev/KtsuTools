// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class PackagesMigrateCpmCommand : AsyncCommand<PackagesMigrateCpmCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Path to the project or solution to migrate")]
		public required string Path { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		AnsiConsole.MarkupLine("[bold]Packages Migrate CPM[/]");
		AnsiConsole.MarkupLine("[yellow]Not yet implemented.[/]");
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
