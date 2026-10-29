// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System;
using System.Collections.Generic;

/// <summary>
/// What followed the first <c>--</c> on a <c>ktools git</c> command line, taken from the process
/// arguments before Spectre.Console.Cli parses them.
/// </summary>
/// <remarks>
/// Spectre still tokenizes the arguments after a <c>--</c>. It reads <c>-1</c> as <c>-</c> plus an
/// option named <c>1</c>, claims <c>-fdx</c> as three ktools options, and rejects <c>-C2</c>
/// outright, so what reaches <see cref="Spectre.Console.Cli.IRemainingArguments.Raw"/> is not what
/// was typed. Holding the tail back from Spectre is the only way to hand git the exact tokens.
/// </remarks>
public sealed class GitPassThrough
{
	private const string GitCommandName = "git";
	private const string Separator = "--";

	private GitPassThrough(IReadOnlyList<string> arguments) => Arguments = arguments;

	/// <summary>Gets a pass-through for a command line that had no <c>--</c> to split at.</summary>
	public static GitPassThrough None { get; } = new([]);

	/// <summary>Gets the arguments that followed the first <c>--</c>, verbatim and in order.</summary>
	public IReadOnlyList<string> Arguments { get; }

	/// <summary>
	/// Splits a <c>ktools git</c> command line at its first <c>--</c>. Any other command line is
	/// returned unchanged, since only <c>git</c> forwards what follows the separator.
	/// </summary>
	/// <param name="args">The process arguments.</param>
	/// <param name="passThrough">The arguments after the separator, or <see cref="None"/>.</param>
	/// <returns>The arguments for Spectre to parse, without the separator or anything after it.</returns>
	public static string[] Split(string[] args, out GitPassThrough passThrough)
	{
		Ensure.NotNull(args);

		int separator = args.Length > 0 && string.Equals(args[0], GitCommandName, StringComparison.Ordinal)
			? Array.IndexOf(args, Separator)
			: -1;

		if (separator < 0)
		{
			passThrough = None;
			return args;
		}

		passThrough = new GitPassThrough(args[(separator + 1)..]);
		return args[..separator];
	}
}
