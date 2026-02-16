// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class RepoUpdatePackagesCommand : AsyncCommand<RepoUpdatePackagesCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Root directory containing repositories")]
		[DefaultValue("c:/dev/ktsu-dev")]
		public string Path { get; init; } = "c:/dev/ktsu-dev";

		[CommandOption("--include-prerelease")]
		[Description("Include prerelease package versions")]
		[DefaultValue(false)]
		public bool IncludePrerelease { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		AnsiConsole.MarkupLine("[bold]Repo Update Packages[/]");
		AnsiConsole.MarkupLine("[yellow]Not yet implemented.[/]");
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
