// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using KtsuTools.Packages;
using Spectre.Console.Cli;

public sealed class PackagesMigrateCpmCommand(PackagesService packagesService) : AsyncCommand<PackagesMigrateCpmCommand.Settings>
{
	private readonly PackagesService packagesService = packagesService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Path to the project or solution to migrate")]
		public required string Path { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		return await packagesService.MigrateToCpmAsync(
			settings.Path,
			CancellationToken.None).ConfigureAwait(false);
	}
}
