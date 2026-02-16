// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class RepoDiscoverCommand : AsyncCommand<RepoDiscoverCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Root directory to scan for repositories")]
		[DefaultValue("c:/dev/ktsu-dev")]
		public string Path { get; init; } = "c:/dev/ktsu-dev";
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		AnsiConsole.MarkupLine("[bold]Repo Discover[/]");
		AnsiConsole.MarkupLine("[yellow]Not yet implemented.[/]");
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
