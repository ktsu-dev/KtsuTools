// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Core.Services.Credentials;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ktsu.AppDataStorage;
using KtsuTools.Core.Services.Settings;
using Spectre.Console;

public class CredentialStore : AppData<CredentialStore>
{
	public Dictionary<string, string> Credentials { get; init; } = [];
}

public class CredentialService(ISettingsService settingsService) : ICredentialService
{
	private readonly ISettingsService _settings = settingsService;
	private CredentialStore? _store;

	public async Task<string?> GetCredentialAsync(string key, string prompt, bool isSecret = true, CancellationToken ct = default)
	{
		CredentialStore store = GetStore();

		if (store.Credentials.TryGetValue(key, out string? existing))
		{
			return existing;
		}

		TextPrompt<string> textPrompt = new(prompt);
		if (isSecret)
		{
			textPrompt.Secret();
		}

		string value = AnsiConsole.Prompt(textPrompt);

		if (!string.IsNullOrWhiteSpace(value))
		{
			store.Credentials[key] = value;
			await _settings.SaveAsync(store).ConfigureAwait(false);
		}

		return value;
	}

	public Task SaveCredentialAsync(string key, string value, CancellationToken ct = default)
	{
		CredentialStore store = GetStore();
		store.Credentials[key] = value;
		return _settings.SaveAsync(store);
	}

	public Task<bool> HasCredentialAsync(string key, CancellationToken ct = default)
	{
		CredentialStore store = GetStore();
		return Task.FromResult(store.Credentials.ContainsKey(key));
	}

	private CredentialStore GetStore() => _store ??= _settings.LoadOrCreate<CredentialStore>();
}
