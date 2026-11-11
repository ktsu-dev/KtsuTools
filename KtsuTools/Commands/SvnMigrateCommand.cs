// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.SvnMigrate;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class SvnMigrateCommand(SvnMigrateService svnMigrateService) : AsyncCommand<SvnMigrateCommand.Settings>
{
	private readonly SvnMigrateService svnMigrateService = svnMigrateService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--svn-url <URL>", isRequired: true)]
		[Description("SVN repository URL to migrate from")]
		[SuppressMessage("Design", "CA1056:UriPropertiesShouldNotBeStrings", Justification = "CLI argument must be a string")]
		public required string SvnUrl { get; init; }

		[CommandOption("--target <PATH>", isRequired: true)]
		[Description("Target directory for the migrated Git repository")]
		public required string TargetPath { get; init; }

		[CommandOption("--authors-file <FILE>")]
		[Description("Path to authors mapping file for SVN-to-Git user mapping")]
		public string? AuthorsFile { get; init; }

		[CommandOption("--preserve-empty-dirs")]
		[Description("Preserve empty directories during migration")]
		[DefaultValue(true)]
		public bool PreserveEmptyDirs { get; init; } = true;

		/// <summary>
		/// Rejects an <c>--svn-url</c> that is not an absolute URL svn can reach, before
		/// <see cref="ExecuteAsync"/> builds a <see cref="Uri"/> from it. A URL pasted without its scheme
		/// (<c>svn.example.com/repo</c>) would otherwise crash with a <see cref="UriFormatException"/>.
		/// </summary>
		public override ValidationResult Validate() =>
			IsSupportedSvnUrl(SvnUrl)
				? ValidationResult.Success()
				: ValidationResult.Error($"--svn-url must be an absolute http, https, svn, svn+ssh or file URL (e.g. https://svn.example.com/repo), got '{SvnUrl}'.");

		private static bool IsSupportedSvnUrl(string? value) =>
			Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
			&& (uri.Scheme is "http" or "https" or "svn" or "file"
				|| uri.Scheme.StartsWith("svn+", StringComparison.Ordinal));
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
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
