// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Merge;

using System.Collections.Generic;
using ktsu.AppDataStorage;

public sealed record MergeBatchEntry
{
	public required string Directory { get; init; }
	public required string Filename { get; init; }
	public string? DiffStyle { get; init; }
}

public class MergeBatchSettings : AppData<MergeBatchSettings>
{
	public Dictionary<string, MergeBatchEntry> Batches { get; init; } = [];
}
