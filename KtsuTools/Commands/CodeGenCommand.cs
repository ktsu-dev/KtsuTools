// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using KtsuTools.CodeGen;
using Spectre.Console.Cli;

public sealed class CodeGenCommand(CodeGenService codeGenService) : AsyncCommand<CodeGenCommand.Settings>
{
	private readonly CodeGenService codeGenService = codeGenService;

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
		Ensure.NotNull(settings);
		return await codeGenService.GenerateAsync(
			settings.InputFile,
			settings.Language,
			settings.OutputFile,
			CancellationToken.None).ConfigureAwait(false);
	}
}
