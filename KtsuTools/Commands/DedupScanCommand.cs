// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.FileDedupe;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class DedupScanCommand(FileDedupeService dedupeService) : AsyncCommand<DedupScanCommand.Settings>
{
	private readonly FileDedupeService dedupeService = dedupeService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("-p|--path <PATH>")]
		[Description("Directory to scan for duplicates")]
		public required string Path { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();

		AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.Path));
		AnsiConsole.MarkupLine($"[bold]Dedup scan[/] - {path.ToString().EscapeMarkup()}");

		DedupePlan plan = await dedupeService.PlanAsync(path, scope.Token).ConfigureAwait(false);

		if (plan.Groups.Count == 0)
		{
			AnsiConsole.MarkupLine("[green]No duplicate groups found.[/]");
			return 0;
		}

		foreach (DuplicateGroup group in plan.Groups)
		{
			AnsiConsole.MarkupLine($"[yellow]{group.Files.Count}[/] files, {group.FileSize} bytes each ({group.Hash[..12]})");
			foreach (string file in group.Files)
			{
				AnsiConsole.MarkupLine($"  {file.EscapeMarkup()}");
			}
		}

		AnsiConsole.MarkupLine($"[blue]{plan.Groups.Count} duplicate group(s), {plan.WastedBytes} wasted byte(s).[/]");
		return 0;
	}
}
