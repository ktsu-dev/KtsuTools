// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
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

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		AnsiConsole.MarkupLine("[bold]Markdown Lint[/]");

		using CtrlCScope scope = new();
		AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.Path));
		int modifiedCount = await markdownService.LintAsync(
			path,
			scope.Token).ConfigureAwait(false);

		AnsiConsole.MarkupLine($"[bold green]Done.[/] {modifiedCount} file(s) linted.");
		return 0;
	}
}
