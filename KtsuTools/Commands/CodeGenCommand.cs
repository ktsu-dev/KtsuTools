// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.CodeGen;
using KtsuTools.Core.UI;
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

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();
		AbsoluteFilePath inputFile = AbsoluteFilePath.Create<AbsoluteFilePath>(Path.GetFullPath(settings.InputFile));
		AbsoluteFilePath? outputFile = settings.OutputFile is null
			? null
			: AbsoluteFilePath.Create<AbsoluteFilePath>(Path.GetFullPath(settings.OutputFile));
		return await codeGenService.GenerateAsync(
			inputFile,
			settings.Language,
			outputFile,
			scope.Token).ConfigureAwait(false);
	}
}
