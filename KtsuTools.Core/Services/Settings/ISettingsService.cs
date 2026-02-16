// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Core.Services.Settings;

using System.Threading.Tasks;
using ktsu.AppDataStorage;

public interface ISettingsService
{
	public T LoadOrCreate<T>() where T : AppData<T>, new();
	public Task SaveAsync<T>(T settings) where T : AppData<T>, new();
}
