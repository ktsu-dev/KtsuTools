// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Sync;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using ktsu.AppDataStorage;

/// <summary>
/// A saved set of sync inputs, so "the files this org keeps in step" is a named thing rather than a flag list to retype.
/// </summary>
public sealed record SyncConfigEntry
{
	/// <summary>
	/// Gets the root path to recursively scan for files.
	/// </summary>
	public required string Path { get; init; }

	/// <summary>
	/// Gets the filename patterns to scan for.
	/// </summary>
	public required Collection<string> Filenames { get; init; }

	/// <summary>
	/// Gets a value indicating whether to push without prompting when every unpushed commit was authored by KtsuTools.
	/// </summary>
	public bool AutoPush { get; init; }

	/// <summary>
	/// Gets the branch to commit onto in each repo, or <see langword="null"/> to use whatever is checked out.
	/// </summary>
	public string? Branch { get; init; }

	/// <summary>
	/// Gets a value indicating whether to open a pull request in each repo whose sync branch was pushed.
	/// </summary>
	public bool OpenPullRequest { get; init; }
}

/// <summary>
/// Persisted sync configurations, stored on the same <see cref="AppData{T}"/> path as saved merge batches.
/// </summary>
public class SyncConfigSettings : AppData<SyncConfigSettings>
{
	/// <summary>
	/// Gets the saved configurations, keyed by name.
	/// </summary>
	public Dictionary<string, SyncConfigEntry> Configs { get; init; } = [];
}
