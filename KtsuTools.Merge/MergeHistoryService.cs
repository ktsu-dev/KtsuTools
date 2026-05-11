// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Merge;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Core.Services.Settings;

public class MergeHistoryService(ISettingsService settingsService)
{
	public const int MaxEntries = 50;

	private readonly ISettingsService _settings = settingsService;
	private MergeHistorySettings? _store;

	public IReadOnlyList<MergeHistoryEntry> List()
	{
		MergeHistorySettings store = GetStore();
		// Most-recent first.
		return [.. store.Entries.OrderByDescending(e => e.Timestamp)];
	}

	public async Task RecordAsync(MergeHistoryEntry entry, CancellationToken ct = default)
	{
		Ensure.NotNull(entry);
		MergeHistorySettings store = GetStore();
		store.Entries.Add(entry);

		while (store.Entries.Count > MaxEntries)
		{
			// Evict the oldest.
			MergeHistoryEntry oldest = store.Entries.OrderBy(e => e.Timestamp).First();
			store.Entries.Remove(oldest);
		}

		await _settings.SaveAsync(store).ConfigureAwait(false);
	}

	public async Task ClearAsync(CancellationToken ct = default)
	{
		MergeHistorySettings store = GetStore();
		if (store.Entries.Count == 0)
		{
			return;
		}

		store.Entries.Clear();
		await _settings.SaveAsync(store).ConfigureAwait(false);
	}

	private MergeHistorySettings GetStore() => _store ??= _settings.LoadOrCreate<MergeHistorySettings>();
}
