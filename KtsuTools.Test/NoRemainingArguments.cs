// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Collections.Generic;
using System.Linq;
using Spectre.Console.Cli;

/// <summary>
/// A <see cref="CommandContext"/> needs remaining arguments even when a verb takes none, so the
/// command tests share this empty one rather than each carrying a copy.
/// </summary>
internal sealed class NoRemainingArguments : IRemainingArguments
{
	public ILookup<string, string?> Parsed { get; } =
		Enumerable.Empty<KeyValuePair<string, string?>>().ToLookup(pair => pair.Key, pair => pair.Value);

	public IReadOnlyList<string> Raw { get; } = [];
}
