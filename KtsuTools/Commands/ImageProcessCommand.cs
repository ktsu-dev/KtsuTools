// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using KtsuTools.Image;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class ImageProcessCommand : AsyncCommand<ImageProcessCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--input <DIRECTORY>")]
		[Description("Input directory containing images")]
		public required string InputPath { get; init; }

		[CommandOption("--output <DIRECTORY>")]
		[Description("Output directory for processed images")]
		public required string OutputPath { get; init; }

		[CommandOption("--color <HEX>")]
		[Description("Target color in hex format")]
		[DefaultValue("#FFFFFF")]
		public string Color { get; init; } = "#FFFFFF";

		[CommandOption("--size <PIXELS>")]
		[Description("Target size in pixels")]
		[DefaultValue(128)]
		public int Size { get; init; }

		[CommandOption("--padding <PIXELS>")]
		[Description("Padding in pixels")]
		[DefaultValue(0)]
		public int Padding { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		AnsiConsole.MarkupLine("[bold]Image Process[/]");

		int processed = await ImageService.ProcessAsync(
			settings.InputPath,
			settings.OutputPath,
			settings.Color,
			settings.Size,
			settings.Padding,
			CancellationToken.None).ConfigureAwait(false);

		return processed > 0 ? 0 : 1;
	}
}
