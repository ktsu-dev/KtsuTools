// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System;
using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.Merge;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class MergeCommand(MergeService mergeService, MergeBatchService batchService, MergeHistoryService historyService) : AsyncCommand<MergeCommand.Settings>
{
	private readonly MergeService mergeService = mergeService;
	private readonly MergeBatchService batchService = batchService;
	private readonly MergeHistoryService historyService = historyService;

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

		[CommandOption("--diff-style <STYLE>")]
		[Description("How to render conflict diffs: side-by-side (default) or git")]
		[DefaultValue("side-by-side")]
		public string DiffStyle { get; init; } = "side-by-side";

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

			if (!DiffStyleParser.TryParse(DiffStyle, out _))
			{
				return ValidationResult.Error($"Unknown --diff-style '{DiffStyle}'. Expected 'side-by-side' or 'git'.");
			}

			return ValidationResult.Success();
		}
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();

		string directoryArg;
		string filenameArg;
		DiffStyle diffStyle;

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

			// Batch's saved DiffStyle wins when present; otherwise fall back to the flag (or its default).
			if (!DiffStyleParser.TryParse(entry.DiffStyle ?? settings.DiffStyle, out diffStyle))
			{
				AnsiConsole.MarkupLine($"[red]Error: batch '{settings.BatchName.EscapeMarkup()}' has unknown DiffStyle '{(entry.DiffStyle ?? string.Empty).EscapeMarkup()}'.[/]");
				return 1;
			}

			AnsiConsole.MarkupLine($"[dim]Running batch '{settings.BatchName.EscapeMarkup()}': {directoryArg.EscapeMarkup()} / {filenameArg.EscapeMarkup()}[/]");
		}
		else
		{
			directoryArg = settings.Directory!;
			filenameArg = settings.Filename!;

			// Settings.Validate already rejects unknown styles, so this is belt-and-braces.
			if (!DiffStyleParser.TryParse(settings.DiffStyle, out diffStyle))
			{
				AnsiConsole.MarkupLine($"[red]Error: unknown --diff-style '{settings.DiffStyle.EscapeMarkup()}'. Expected 'side-by-side' or 'git'.[/]");
				return 1;
			}
		}

		AbsoluteDirectoryPath directory = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(directoryArg));
		int exitCode = await mergeService.RunMergeAsync(
			directory,
			filenameArg,
			diffStyle,
			scope.Token).ConfigureAwait(false);

		bool isBatch = !string.IsNullOrWhiteSpace(settings.BatchName);
		// Direct invocations record every run; batch dispatches only record on success
		// (a failed batch usually means the saved config is stale, not user input worth recalling).
		if (!isBatch || exitCode == 0)
		{
			MergeHistoryEntry historyEntry = new()
			{
				Timestamp = DateTimeOffset.UtcNow,
				Directory = directoryArg,
				Filename = filenameArg,
				DiffStyle = DiffStyleParser.ToCanonicalString(diffStyle),
				BatchName = isBatch ? settings.BatchName : null,
				ExitCode = exitCode,
			};
			await historyService.RecordAsync(historyEntry, scope.Token).ConfigureAwait(false);
		}

		return exitCode;
	}
}
