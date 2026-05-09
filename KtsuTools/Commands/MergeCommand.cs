// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.Merge;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class MergeCommand(MergeService mergeService, MergeBatchService batchService) : AsyncCommand<MergeCommand.Settings>
{
	private readonly MergeService mergeService = mergeService;
	private readonly MergeBatchService batchService = batchService;

	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "[directory]")]
		[Description("Directory containing files to merge")]
		public string? Directory { get; init; }

		[CommandArgument(1, "[filename]")]
		[Description("Filename pattern to merge")]
		public string? Filename { get; init; }

		[CommandOption("--batch <NAME>")]
		[Description("Run a saved batch by name (use 'merge-batch save' to create one)")]
		public string? BatchName { get; init; }

		public override ValidationResult Validate()
		{
			bool hasDirectory = !string.IsNullOrWhiteSpace(Directory);
			bool hasFilename = !string.IsNullOrWhiteSpace(Filename);
			bool hasBatch = !string.IsNullOrWhiteSpace(BatchName);

			if (hasDirectory != hasFilename)
			{
				return ValidationResult.Error("Both <directory> and <filename> must be provided together.");
			}

			bool hasPositional = hasDirectory && hasFilename;

			if (hasPositional && hasBatch)
			{
				return ValidationResult.Error("--batch is mutually exclusive with positional <directory> <filename>.");
			}

			if (!hasPositional && !hasBatch)
			{
				return ValidationResult.Error("Provide either <directory> <filename> or --batch <name>.");
			}

			return ValidationResult.Success();
		}
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();

		string directoryArg;
		string filenameArg;

		if (!string.IsNullOrWhiteSpace(settings.BatchName))
		{
			MergeBatchEntry? entry = batchService.Get(settings.BatchName);
			if (entry is null)
			{
				AnsiConsole.MarkupLine($"[red]Error: no batch named '{settings.BatchName.EscapeMarkup()}'. Run 'merge-batch list' to see saved batches.[/]");
				return 1;
			}

			directoryArg = entry.Directory;
			filenameArg = entry.Filename;
			AnsiConsole.MarkupLine($"[dim]Running batch '{settings.BatchName.EscapeMarkup()}': {directoryArg.EscapeMarkup()} / {filenameArg.EscapeMarkup()}[/]");
		}
		else
		{
			directoryArg = settings.Directory!;
			filenameArg = settings.Filename!;
		}

		AbsoluteDirectoryPath directory = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(directoryArg));
		return await mergeService.RunMergeAsync(
			directory,
			filenameArg,
			scope.Token).ConfigureAwait(false);
	}
}
