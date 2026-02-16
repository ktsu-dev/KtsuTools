// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using KtsuTools.SvnMigrate;
using Spectre.Console.Cli;

public sealed class SvnMigrateCommand(SvnMigrateService svnMigrateService) : AsyncCommand<SvnMigrateCommand.Settings>
{
	private readonly SvnMigrateService svnMigrateService = svnMigrateService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--svn-url <URL>")]
		[Description("SVN repository URL to migrate from")]
		[SuppressMessage("Design", "CA1056:UriPropertiesShouldNotBeStrings", Justification = "CLI argument must be a string")]
		public required string SvnUrl { get; init; }

		[CommandOption("--target <PATH>")]
		[Description("Target directory for the migrated Git repository")]
		public required string TargetPath { get; init; }

		[CommandOption("--authors-file <FILE>")]
		[Description("Path to authors mapping file for SVN-to-Git user mapping")]
		public string? AuthorsFile { get; init; }

		[CommandOption("--preserve-empty-dirs")]
		[Description("Preserve empty directories during migration")]
		[DefaultValue(true)]
		public bool PreserveEmptyDirs { get; init; } = true;
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		return await svnMigrateService.MigrateAsync(
			new Uri(settings.SvnUrl),
			settings.TargetPath,
			settings.AuthorsFile,
			settings.PreserveEmptyDirs,
			CancellationToken.None).ConfigureAwait(false);
	}
}
