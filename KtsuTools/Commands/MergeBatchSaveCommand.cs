// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using KtsuTools.Core.UI;
using KtsuTools.Merge;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class MergeBatchSaveCommand(MergeBatchService batchService) : AsyncCommand<MergeBatchSaveCommand.Settings>
{
	private readonly MergeBatchService batchService = batchService;

	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<name>")]
		[Description("Name to save this batch under")]
		public required string Name { get; init; }

		[CommandArgument(1, "<directory>")]
		[Description("Directory containing files to merge")]
		public required string Directory { get; init; }

		[CommandArgument(2, "<filename>")]
		[Description("Filename pattern to merge")]
		public required string Filename { get; init; }

		[CommandOption("--diff-style <STYLE>")]
		[Description("Diff style to use when running this batch (reserved for future use)")]
		public string? DiffStyle { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();

		MergeBatchEntry entry = new()
		{
			Directory = settings.Directory,
			Filename = settings.Filename,
			DiffStyle = settings.DiffStyle,
		};

		await batchService.SaveAsync(settings.Name, entry, scope.Token).ConfigureAwait(false);
		AnsiConsole.MarkupLine($"[green]Saved batch '{settings.Name.EscapeMarkup()}'.[/]");
		return 0;
	}
}
