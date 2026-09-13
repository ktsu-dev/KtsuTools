// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Repo;

using System.Collections.ObjectModel;
using ktsu.AppDataStorage;

public class RepoCacheSettings : AppData<RepoCacheSettings>
{
	public Collection<string> Repositories { get; init; } = [];
	public Collection<string> Solutions { get; init; } = [];
}
