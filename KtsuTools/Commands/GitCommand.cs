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

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
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
				$"{string.Join(", ", dropped)} would be parsed as a {CliIdentity.Name} option, not passed to git. " +
				$"Put git flags after a '--' separator, for example: {BuildSeparatorSuggestion(settings, dropped)}");
			return 1;
		}

		string[] args = [.. settings.Args, .. context.Remaining.Raw];

		if (args.Length == 0)
		{
			ErrorDisplay.ShowError($"Specify a git command, for example: {CliIdentity.Name} git status");
			return 1;
		}

		AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.Path));

		return await repoService.RunGitAsync(
			path,
			args,
			// Match what Spectre does with its own chrome, and what git itself does with color.ui=auto:
			// colorize for a terminal, stay plain once the output is piped or redirected to a file.
			color: !settings.NoColor && !Console.IsOutputRedirected,
			scope.Token).ConfigureAwait(false);
	}

	/// <summary>
	/// Builds the command line to suggest when git flags were typed without a '--' separator: the
	/// same invocation, with this tool's own options kept ahead of the separator so that pasting it
	/// runs git in the same place, and the git command with its flags after it.
	/// </summary>
	/// <param name="settings">The options and positional arguments the user typed.</param>
	/// <param name="dropped">The flags Spectre claimed, which belong to git.</param>
	/// <returns>A command line that can be pasted and run as-is.</returns>
	internal static string BuildSeparatorSuggestion(Settings settings, IEnumerable<string> dropped)
	{
		Ensure.NotNull(settings);
		Ensure.NotNull(dropped);

		List<string> tokens = [CliIdentity.Name, "git"];

		if (settings.Path != ".")
		{
			tokens.Add("--path");
			tokens.Add(QuoteIfNeeded(settings.Path));
		}

		if (settings.NoColor)
		{
			tokens.Add("--no-color");
		}

		tokens.Add("--");
		tokens.AddRange(settings.Args.Select(QuoteIfNeeded));
		tokens.AddRange(dropped.Select(QuoteIfNeeded));

		return string.Join(' ', tokens);
	}

	private static string QuoteIfNeeded(string token) =>
		token.Length > 0 && !token.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '`' or '$')
			? token
			: $"\"{token.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
