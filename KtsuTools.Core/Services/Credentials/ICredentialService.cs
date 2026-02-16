// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Core.Services.Credentials;

using System.Threading;
using System.Threading.Tasks;

public interface ICredentialService
{
	public Task<string?> GetCredentialAsync(string key, string prompt, bool isSecret = true, CancellationToken ct = default);
	public Task SaveCredentialAsync(string key, string value, CancellationToken ct = default);
	public Task<bool> HasCredentialAsync(string key, CancellationToken ct = default);
}
