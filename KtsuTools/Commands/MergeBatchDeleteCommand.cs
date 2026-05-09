// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using KtsuTools.Core.UI;
using KtsuTools.Merge;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class MergeBatchDeleteCommand(MergeBatchService batchService) : AsyncCommand<MergeBatchDeleteCommand.Settings>
{
	private readonly MergeBatchService batchService = batchService;

	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<name>")]
		[Description("Name of the batch to delete")]
		public required string Name { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();

		bool removed = await batchService.DeleteAsync(settings.Name, scope.Token).ConfigureAwait(false);
		if (!removed)
		{
			AnsiConsole.MarkupLine($"[red]No batch named '{settings.Name.EscapeMarkup()}'.[/]");
			return 1;
		}

		AnsiConsole.MarkupLine($"[green]Deleted batch '{settings.Name.EscapeMarkup()}'.[/]");
		return 0;
	}
}
