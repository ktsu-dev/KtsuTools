// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
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

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		AnsiConsole.MarkupLine("[bold]Markdown Clean[/]");

		using CtrlCScope scope = new();
		AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.Path));
		int modifiedCount = await markdownService.CleanAsync(
			path,
			settings.ApplyLinting,
			settings.StandardizeLineEndings,
			scope.Token).ConfigureAwait(false);

		AnsiConsole.MarkupLine($"[bold green]Done.[/] {modifiedCount} file(s) modified.");
		return 0;
	}
}
