// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using Spectre.Console.Cli;

/// <summary>
/// The directory every <c>dedup</c> verb works on. The verbs share one settings type so the
/// option is declared, and enforced as required, in one place.
/// </summary>
public class DedupSettings : CommandSettings
{
	[CommandOption("-p|--path <PATH>", isRequired: true)]
	[Description("Directory to scan for duplicates")]
	public required string Path { get; init; }
}
