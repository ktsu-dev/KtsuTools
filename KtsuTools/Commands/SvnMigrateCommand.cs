// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
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

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();
		AbsoluteDirectoryPath targetPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.TargetPath));
		AbsoluteFilePath? authorsFile = string.IsNullOrWhiteSpace(settings.AuthorsFile)
			? null
			: AbsoluteFilePath.Create<AbsoluteFilePath>(Path.GetFullPath(settings.AuthorsFile));
		return await svnMigrateService.MigrateAsync(
			new Uri(settings.SvnUrl),
			targetPath,
			authorsFile,
			settings.PreserveEmptyDirs,
			scope.Token).ConfigureAwait(false);
	}
}
