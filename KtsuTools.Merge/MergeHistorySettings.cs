// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Merge;

using System;
using System.Collections.ObjectModel;
using ktsu.AppDataStorage;

public sealed record MergeHistoryEntry
{
	public required DateTimeOffset Timestamp { get; init; }
	public required string Directory { get; init; }
	public required string Filename { get; init; }
	public required string DiffStyle { get; init; }
	public string? BatchName { get; init; }
	public required int ExitCode { get; init; }
}

public class MergeHistorySettings : AppData<MergeHistorySettings>
{
	public Collection<MergeHistoryEntry> Entries { get; init; } = [];
}
