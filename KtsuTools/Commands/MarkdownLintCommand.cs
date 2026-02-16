// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using KtsuTools.Markdown;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class MarkdownLintCommand(MarkdownService markdownService) : AsyncCommand<MarkdownLintCommand.Settings>
{
	private readonly MarkdownService markdownService = markdownService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Path to markdown files to lint")]
		public required string Path { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		AnsiConsole.MarkupLine("[bold]Markdown Lint[/]");

		int modifiedCount = await markdownService.LintAsync(
			settings.Path,
			CancellationToken.None).ConfigureAwait(false);

		AnsiConsole.MarkupLine($"[bold green]Done.[/] {modifiedCount} file(s) linted.");
		return 0;
	}
}
