// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Sync;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Core.Services.Settings;

/// <summary>
/// Reads and writes named sync configurations, mirroring the saved-batch machinery behind <c>merge-batch</c>.
/// </summary>
/// <param name="settingsService">The settings store backing the saved configurations.</param>
public class SyncConfigService(ISettingsService settingsService)
{
	private readonly ISettingsService _settings = settingsService;
	private SyncConfigSettings? _store;

	/// <summary>
	/// Lists every saved configuration, keyed by name.
	/// </summary>
	/// <returns>The saved configurations.</returns>
	public IReadOnlyDictionary<string, SyncConfigEntry> List() => GetStore().Configs;

	/// <summary>
	/// Gets a saved configuration by name.
	/// </summary>
	/// <param name="name">The configuration name.</param>
	/// <returns>The configuration, or <see langword="null"/> when no configuration of that name is saved.</returns>
	public SyncConfigEntry? Get(string name) =>
		GetStore().Configs.TryGetValue(name, out SyncConfigEntry? entry) ? entry : null;

	/// <summary>
	/// Saves a configuration under a name, replacing any configuration already saved under it.
	/// </summary>
	/// <param name="name">The configuration name.</param>
	/// <param name="entry">The configuration to save.</param>
	/// <param name="ct">A token to cancel the save.</param>
	/// <returns>A task that completes once the configuration is persisted.</returns>
	public async Task SaveAsync(string name, SyncConfigEntry entry, CancellationToken ct = default)
	{
		Ensure.NotNull(name);
		Ensure.NotNull(entry);
		ct.ThrowIfCancellationRequested();
		SyncConfigSettings store = GetStore();
		store.Configs[name] = entry;
		await _settings.SaveAsync(store).ConfigureAwait(false);
	}

	/// <summary>
	/// Deletes a saved configuration.
	/// </summary>
	/// <param name="name">The configuration name.</param>
	/// <param name="ct">A token to cancel the delete.</param>
	/// <returns><see langword="true"/> when a configuration was removed, <see langword="false"/> when none was saved under that name.</returns>
	public async Task<bool> DeleteAsync(string name, CancellationToken ct = default)
	{
		Ensure.NotNull(name);
		ct.ThrowIfCancellationRequested();
		SyncConfigSettings store = GetStore();
		if (!store.Configs.Remove(name))
		{
			return false;
		}

		await _settings.SaveAsync(store).ConfigureAwait(false);
		return true;
	}

	private SyncConfigSettings GetStore() => _store ??= _settings.LoadOrCreate<SyncConfigSettings>();
}
