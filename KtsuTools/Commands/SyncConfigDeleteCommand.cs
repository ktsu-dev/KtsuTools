// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Core.UI;
using KtsuTools.Sync;
using Spectre.Console;
using Spectre.Console.Cli;

/// <summary>
/// Command that deletes a saved sync configuration.
/// </summary>
public sealed class SyncConfigDeleteCommand(SyncConfigService configService) : AsyncCommand<SyncConfigDeleteCommand.Settings>
{
	private readonly SyncConfigService configService = configService;

	/// <summary>
	/// Settings for the sync-config delete command.
	/// </summary>
	public sealed class Settings : CommandSettings
	{
		/// <summary>
		/// Gets the name of the configuration to delete.
		/// </summary>
		[CommandArgument(0, "<name>")]
		[Description("Name of the configuration to delete")]
		public required string Name { get; init; }
	}

	/// <inheritdoc/>
	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();

		bool removed = await configService.DeleteAsync(settings.Name, scope.Token).ConfigureAwait(false);
		if (!removed)
		{
			AnsiConsole.MarkupLine($"[red]No sync configuration named '{settings.Name.EscapeMarkup()}'.[/]");
			return 1;
		}

		AnsiConsole.MarkupLine($"[green]Deleted sync configuration '{settings.Name.EscapeMarkup()}'.[/]");
		return 0;
	}
}
