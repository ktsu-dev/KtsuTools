// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Core.Services.Git;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IGitService
{
	public Task<bool> PullAsync(string repoPath, CancellationToken ct = default);
	public Task<bool> CommitAsync(string repoPath, string message, CancellationToken ct = default);
	public Task<bool> PushAsync(string repoPath, CancellationToken ct = default);
	public Task<IReadOnlyList<string>> GetStatusAsync(string repoPath, CancellationToken ct = default);
	public Task<string> GetCurrentBranchAsync(string repoPath, CancellationToken ct = default);
	public Task<bool> CloneAsync(Uri url, string targetPath, CancellationToken ct = default);
	public Task<bool> IsRepositoryAsync(string path, CancellationToken ct = default);
}
