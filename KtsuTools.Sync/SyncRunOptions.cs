// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Sync;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

/// <summary>
/// The inputs a sync run needs, after a saved configuration and the command-line flags have been reconciled.
/// </summary>
public sealed record SyncRunOptions
{
	/// <summary>
	/// Gets the root path to recursively scan, or the empty string when neither the flag nor the configuration supplied one.
	/// </summary>
	public required string Path { get; init; }

	/// <summary>
	/// Gets the filename patterns to scan for, comma-separated entries already expanded and de-duplicated.
	/// </summary>
	public required Collection<string> Filenames { get; init; }

	/// <summary>
	/// Gets a value indicating whether to push without prompting when every unpushed commit was authored by KtsuTools.
	/// </summary>
	public required bool AutoPush { get; init; }

	/// <summary>
	/// Gets the branch to commit onto in each repo, or the empty string to use whatever is checked out.
	/// </summary>
	public required string Branch { get; init; }

	/// <summary>
	/// Gets a value indicating whether to open a pull request in each repo whose sync branch was pushed.
	/// </summary>
	public required bool OpenPullRequest { get; init; }
}

/// <summary>
/// Reconciles a saved <see cref="SyncConfigEntry"/> with the flags given on the command line.
/// </summary>
public static class SyncConfigResolver
{
	/// <summary>
	/// Expands filename arguments, splitting comma-separated entries and dropping blanks and duplicates.
	/// </summary>
	/// <param name="raw">The raw filename arguments, as repeated flags or comma-separated lists.</param>
	/// <returns>The expanded filename patterns.</returns>
	public static Collection<string> ExpandFilenames(IEnumerable<string?>? raw) =>
		raw is null
			? []
			: [.. raw
				.Where(v => v is not null)
				.SelectMany(v => v!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				.Where(v => !string.IsNullOrWhiteSpace(v))
				.Distinct(StringComparer.Ordinal)];

	/// <summary>
	/// Resolves the inputs for a run, letting an explicitly-supplied flag override the saved configuration.
	/// </summary>
	/// <param name="saved">The saved configuration, or <see langword="null"/> when the run named none.</param>
	/// <param name="path">The <c>--path</c> flag, empty or whitespace when not supplied.</param>
	/// <param name="filenames">The <c>--filename</c> flags, empty when not supplied.</param>
	/// <param name="autoPush">The <c>--auto-push</c> flag.</param>
	/// <param name="branch">The <c>--branch</c> flag, empty or whitespace when not supplied.</param>
	/// <param name="openPullRequest">The <c>--pr</c> flag.</param>
	/// <returns>The reconciled inputs.</returns>
	/// <remarks>
	/// The string options are overridden when the flag carries a value, so a saved path or branch survives a run that
	/// does not name one. The two boolean flags can only turn a saved <see langword="false"/> into
	/// <see langword="true"/>: a flag that is absent is indistinguishable from one passed as <c>false</c>, so a
	/// configuration saved with <c>--auto-push</c> keeps it. Save a second configuration for the quieter run.
	/// </remarks>
	public static SyncRunOptions Resolve(
		SyncConfigEntry? saved,
		string? path,
		IEnumerable<string?>? filenames,
		bool autoPush,
		string? branch,
		bool openPullRequest)
	{
		Collection<string> flagFilenames = ExpandFilenames(filenames);

		return new SyncRunOptions
		{
			Path = !string.IsNullOrWhiteSpace(path) ? path : saved?.Path ?? string.Empty,
			Filenames = flagFilenames.Count > 0 ? flagFilenames : ExpandFilenames(saved?.Filenames),
			AutoPush = autoPush || (saved?.AutoPush ?? false),
			Branch = !string.IsNullOrWhiteSpace(branch) ? branch : saved?.Branch ?? string.Empty,
			OpenPullRequest = openPullRequest || (saved?.OpenPullRequest ?? false),
		};
	}
}
