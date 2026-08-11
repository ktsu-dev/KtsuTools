// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Core.Services.Git;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class GitService : IGitService
{
	public async Task<bool> PullAsync(string repoPath, CancellationToken ct = default)
	{
		GitResult result = await GitCli.RunInAsync(repoPath, ct, "pull").ConfigureAwait(false);
		return result.Succeeded;
	}

	public async Task<bool> CommitAsync(string repoPath, string message, CancellationToken ct = default)
	{
		GitResult staged = await GitCli.RunInAsync(repoPath, ct, "add", "--all").ConfigureAwait(false);
		if (!staged.Succeeded)
		{
			return false;
		}

		// git exits non-zero when there is nothing staged, which matches the previous behaviour of
		// reporting failure for an empty commit.
		GitResult committed = await GitCli.RunInAsync(repoPath, ct, "commit", "-m", message).ConfigureAwait(false);
		return committed.Succeeded;
	}

	public async Task<bool> PushAsync(string repoPath, CancellationToken ct = default)
	{
		// HEAD rather than a branch name so a detached head fails loudly instead of pushing the
		// wrong ref, and origin explicitly because that is the remote the previous version used.
		GitResult result = await GitCli.RunInAsync(repoPath, ct, "push", "origin", "HEAD").ConfigureAwait(false);
		return result.Succeeded;
	}

	public async Task<IReadOnlyList<string>> GetStatusAsync(string repoPath, CancellationToken ct = default)
	{
		GitResult result = await GitCli.RunInAsync(repoPath, ct, "status", "--porcelain").ConfigureAwait(false);
		if (!result.Succeeded)
		{
			return [];
		}

		List<string> entries = [];
		foreach (string line in result.OutputLines)
		{
			// Porcelain v1 is a two character status code, a space, then the path.
			if (line.Length > 3)
			{
				entries.Add($"{line[..2].Trim()}: {line[3..]}");
			}
		}

		return entries;
	}

	public async Task<string> GetCurrentBranchAsync(string repoPath, CancellationToken ct = default)
	{
		GitResult result = await GitCli.RunInAsync(repoPath, ct, "rev-parse", "--abbrev-ref", "HEAD").ConfigureAwait(false);
		return result.Succeeded ? result.OutputText : string.Empty;
	}

	public async Task<bool> CloneAsync(Uri url, string targetPath, CancellationToken ct = default)
	{
		Ensure.NotNull(url);

		GitResult result = await GitCli.RunAsync(["clone", url.AbsoluteUri, targetPath], ct).ConfigureAwait(false);
		return result.Succeeded;
	}

	public async Task<bool> IsRepositoryAsync(string path, CancellationToken ct = default)
	{
		if (!Directory.Exists(path))
		{
			return false;
		}

		GitResult result = await GitCli.RunInAsync(path, ct, "rev-parse", "--is-inside-work-tree").ConfigureAwait(false);
		return result.Succeeded && result.OutputText.Equals("true", StringComparison.OrdinalIgnoreCase);
	}
}
