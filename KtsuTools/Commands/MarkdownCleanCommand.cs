// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using KtsuTools.Markdown;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class MarkdownCleanCommand(MarkdownService markdownService) : AsyncCommand<MarkdownCleanCommand.Settings>
{
	private readonly MarkdownService markdownService = markdownService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Path to markdown files to clean")]
		public required string Path { get; init; }

		[CommandOption("--lint")]
		[Description("Apply markdown linting rules")]
		[DefaultValue(true)]
		public bool ApplyLinting { get; init; } = true;

		[CommandOption("--normalize-line-endings")]
		[Description("Standardize line endings to platform default")]
		[DefaultValue(true)]
		public bool StandardizeLineEndings { get; init; } = true;
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		AnsiConsole.MarkupLine("[bold]Markdown Clean[/]");

		int modifiedCount = await markdownService.CleanAsync(
			settings.Path,
			settings.ApplyLinting,
			settings.StandardizeLineEndings,
			CancellationToken.None).ConfigureAwait(false);

		AnsiConsole.MarkupLine($"[bold green]Done.[/] {modifiedCount} file(s) modified.");
		return 0;
	}
}
