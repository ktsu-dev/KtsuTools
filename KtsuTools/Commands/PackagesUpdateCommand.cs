// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using KtsuTools.Packages;
using Spectre.Console.Cli;

public sealed class PackagesUpdateCommand(PackagesService packagesService) : AsyncCommand<PackagesUpdateCommand.Settings>
{
	private readonly PackagesService packagesService = packagesService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Path to the project or solution")]
		public required string Path { get; init; }

		[CommandOption("--what-if")]
		[Description("Preview changes without applying them")]
		[DefaultValue(false)]
		public bool WhatIf { get; init; }

		[CommandOption("--include-prerelease")]
		[Description("Include prerelease package versions")]
		[DefaultValue(false)]
		public bool IncludePrerelease { get; init; }

		[CommandOption("--source <SOURCE>")]
		[Description("Package source to use")]
		[DefaultValue("nuget")]
		public string Source { get; init; } = "nuget";
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		return await packagesService.UpdateAsync(
			settings.Path,
			settings.WhatIf,
			settings.IncludePrerelease,
			settings.Source,
			CancellationToken.None).ConfigureAwait(false);
	}
}
