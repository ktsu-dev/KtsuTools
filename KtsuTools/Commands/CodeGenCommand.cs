// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class CodeGenCommand : AsyncCommand<CodeGenCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--input <FILE>")]
		[Description("Input file for code generation")]
		public required string InputFile { get; init; }

		[CommandOption("--lang <LANGUAGE>")]
		[Description("Target programming language")]
		[DefaultValue("csharp")]
		public string Language { get; init; } = "csharp";

		[CommandOption("--output <FILE>")]
		[Description("Output file path")]
		public string? OutputFile { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		AnsiConsole.MarkupLine("[bold]Code Generation[/]");
		AnsiConsole.MarkupLine("[yellow]Not yet implemented.[/]");
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
