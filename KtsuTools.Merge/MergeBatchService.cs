// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Merge;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Core.Services.Settings;

public class MergeBatchService(ISettingsService settingsService)
{
	private readonly ISettingsService _settings = settingsService;
	private MergeBatchSettings? _store;

	public IReadOnlyDictionary<string, MergeBatchEntry> List() => GetStore().Batches;

	public MergeBatchEntry? Get(string name) =>
		GetStore().Batches.TryGetValue(name, out MergeBatchEntry? entry) ? entry : null;

	public async Task SaveAsync(string name, MergeBatchEntry entry, CancellationToken ct = default)
	{
		Ensure.NotNull(name);
		Ensure.NotNull(entry);
		ct.ThrowIfCancellationRequested();
		MergeBatchSettings store = GetStore();
		store.Batches[name] = entry;
		await _settings.SaveAsync(store).ConfigureAwait(false);
	}

	public async Task<bool> DeleteAsync(string name, CancellationToken ct = default)
	{
		Ensure.NotNull(name);
		ct.ThrowIfCancellationRequested();
		MergeBatchSettings store = GetStore();
		if (!store.Batches.Remove(name))
		{
			return false;
		}

		await _settings.SaveAsync(store).ConfigureAwait(false);
		return true;
	}

	private MergeBatchSettings GetStore() => _store ??= _settings.LoadOrCreate<MergeBatchSettings>();
}
