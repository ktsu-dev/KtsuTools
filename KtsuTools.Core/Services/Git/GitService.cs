// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Core.Services.Git;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;

public class GitService : IGitService
{
	public Task<bool> PullAsync(string repoPath, CancellationToken ct = default) =>
		Task.Run(() =>
		{
			try
			{
				using Repository repo = new(repoPath);
				Signature signature = repo.Config.BuildSignature(DateTimeOffset.Now);
				PullOptions options = new()
				{
					FetchOptions = new FetchOptions(),
				};
				Commands.Pull(repo, signature, options);
				return true;
			}
			catch (LibGit2SharpException)
			{
				return false;
			}
		}, ct);

	public Task<bool> CommitAsync(string repoPath, string message, CancellationToken ct = default) =>
		Task.Run(() =>
		{
			try
			{
				using Repository repo = new(repoPath);
				Commands.Stage(repo, "*");
				Signature signature = repo.Config.BuildSignature(DateTimeOffset.Now);
				repo.Commit(message, signature, signature);
				return true;
			}
			catch (LibGit2SharpException)
			{
				return false;
			}
		}, ct);

	public Task<bool> PushAsync(string repoPath, CancellationToken ct = default) =>
		Task.Run(() =>
		{
			try
			{
				using Repository repo = new(repoPath);
				Remote? remote = repo.Network.Remotes["origin"];
				if (remote is null)
				{
					return false;
				}

				Branch branch = repo.Head;
				repo.Network.Push(remote, branch.CanonicalName, new PushOptions());
				return true;
			}
			catch (LibGit2SharpException)
			{
				return false;
			}
		}, ct);

	public Task<IReadOnlyList<string>> GetStatusAsync(string repoPath, CancellationToken ct = default) =>
		Task.Run<IReadOnlyList<string>>(() =>
		{
			try
			{
				using Repository repo = new(repoPath);
				return [.. repo.RetrieveStatus().Select(e => $"{e.State}: {e.FilePath}")];
			}
			catch (LibGit2SharpException)
			{
				return [];
			}
		}, ct);

	public Task<string> GetCurrentBranchAsync(string repoPath, CancellationToken ct = default) =>
		Task.Run(() =>
		{
			try
			{
				using Repository repo = new(repoPath);
				return repo.Head.FriendlyName;
			}
			catch (LibGit2SharpException)
			{
				return string.Empty;
			}
		}, ct);

	public Task<bool> CloneAsync(Uri url, string targetPath, CancellationToken ct = default) =>
		Task.Run(() =>
		{
			try
			{
				Repository.Clone(url.AbsoluteUri, targetPath);
				return true;
			}
			catch (LibGit2SharpException)
			{
				return false;
			}
		}, ct);

	public Task<bool> IsRepositoryAsync(string path, CancellationToken ct = default) =>
		Task.Run(() => Repository.IsValid(path), ct);
}
