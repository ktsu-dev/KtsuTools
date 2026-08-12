// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Core.Services.Settings;

using System.Threading.Tasks;
using ktsu.AppDataStorage;

public class SettingsService : ISettingsService
{
	public T LoadOrCreate<T>() where T : AppData<T>, new() =>
		AppData<T>.LoadOrCreate();

	public Task SaveAsync<T>(T settings) where T : AppData<T>, new()
	{
		Ensure.NotNull(settings);
		settings.Save();
		return Task.CompletedTask;
	}
}
