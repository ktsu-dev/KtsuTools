// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.Repo;
using Spectre.Console.Cli;

public sealed class GitCommand(RepoService repoService) : AsyncCommand<GitCommand.Settings>
{
	private readonly RepoService repoService = repoService;

	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "[ARGS]")]
		[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Spectre.Console.Cli binds multi-value arguments only to an array.")]
		[Description("The git command to run, for example 'status'. Put anything with flags after a '--' separator")]
		public string[] Args { get; init; } = [];

		[CommandOption("--path <PATH>")]
		[Description("Root directory to search for repositories")]
		[DefaultValue(".")]
		public string Path { get; init; } = ".";

		[CommandOption("--no-color")]
		[Description("Never colorize git's output, even when writing to a terminal")]
		public bool NoColor { get; init; }
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(context);
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();

		// Spectre claims any option it doesn't recognize, such as --short, and parks it in
		// Remaining.Parsed with its position and short-or-long form lost, so rebuilding the git
		// command line from Parsed would silently run something other than what was typed. Only a
		// literal '--' preserves the flags, coming back verbatim and in order through Remaining.Raw.
		// Spectre mirrors those into Parsed too, so a flag is genuinely lost only when it shows up
		// in Parsed without showing up in Raw, which is exactly the forgotten-separator case.
		HashSet<string> rawTokens = new(StringComparer.Ordinal);
		foreach (string token in context.Remaining.Raw)
		{
			rawTokens.Add(token);
			int equals = token.IndexOf('=', StringComparison.Ordinal);
			if (equals > 0)
			{
				rawTokens.Add(token[..equals]);
			}
		}

		string[] dropped = [.. context.Remaining.Parsed
			.Select(group => group.Key)
			.Where(key => !rawTokens.Contains(key))];

		if (dropped.Length > 0)
		{
			ErrorDisplay.ShowError(
				$"{string.Join(", ", dropped)} would be parsed as a ktsu option, not passed to git. " +
				$"Put git flags after a '--' separator, for example: ktsu git -- {string.Join(' ', settings.Args)} {string.Join(' ', dropped)}");
			return 1;
		}

		string[] args = [.. settings.Args, .. context.Remaining.Raw];

		AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.Path));

		return await repoService.RunGitAsync(
			path,
			args,
			// Match what Spectre does with its own chrome, and what git itself does with color.ui=auto:
			// colorize for a terminal, stay plain once the output is piped or redirected to a file.
			color: !settings.NoColor && !Console.IsOutputRedirected,
			scope.Token).ConfigureAwait(false);
	}
}
