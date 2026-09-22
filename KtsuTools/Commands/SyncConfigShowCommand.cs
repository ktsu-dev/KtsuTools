// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.Threading;
using KtsuTools.Sync;
using Spectre.Console;
using Spectre.Console.Cli;

/// <summary>
/// Command that shows the details of a saved sync configuration.
/// </summary>
public sealed class SyncConfigShowCommand(SyncConfigService configService) : Command<SyncConfigShowCommand.Settings>
{
	private readonly SyncConfigService configService = configService;

	/// <summary>
	/// Settings for the sync-config show command.
	/// </summary>
	public sealed class Settings : CommandSettings
	{
		/// <summary>
		/// Gets the name of the configuration to show.
		/// </summary>
		[CommandArgument(0, "<name>")]
		[Description("Name of the configuration to show")]
		public required string Name { get; init; }
	}

	/// <inheritdoc/>
	protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		SyncConfigEntry? entry = configService.Get(settings.Name);

		if (entry is null)
		{
			AnsiConsole.MarkupLine($"[red]No sync configuration named '{settings.Name.EscapeMarkup()}'.[/]");
			return 1;
		}

		AnsiConsole.MarkupLine($"[bold]Sync configuration:[/] {settings.Name.EscapeMarkup()}");
		AnsiConsole.MarkupLine($"  [dim]Path:[/]      {entry.Path.EscapeMarkup()}");
		AnsiConsole.MarkupLine($"  [dim]Filenames:[/] {string.Join(", ", entry.Filenames).EscapeMarkup()}");
		AnsiConsole.MarkupLine($"  [dim]Branch:[/]    {(entry.Branch ?? "(checked out)").EscapeMarkup()}");
		AnsiConsole.MarkupLine($"  [dim]Auto push:[/] {(entry.AutoPush ? "yes" : "no")}");
		AnsiConsole.MarkupLine($"  [dim]Pull request:[/] {(entry.OpenPullRequest ? "yes" : "no")}");
		return 0;
	}
}
