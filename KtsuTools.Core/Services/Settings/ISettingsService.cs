// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Core.Services.Settings;

using System.Threading.Tasks;
using ktsu.AppDataStorage;

public interface ISettingsService
{
	public T LoadOrCreate<T>() where T : AppData<T>, new();
	public Task SaveAsync<T>(T settings) where T : AppData<T>, new();
}
