// Copyright (c) 2023-2026 ktsu-dev contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ktsu.KtsuTools.Test")]
// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.FileDedupe;

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO.Enumeration;
using System.Security.Cryptography;
using ktsu.Semantics.Paths;
using Spectre.Console;

/// <summary>
/// A set of files with byte-identical content, identified by SHA256.
/// </summary>
public sealed record DuplicateGroup(string Hash, long FileSize, Collection<string> Files);

/// <summary>
/// Result of a dedupe planning pass.
/// </summary>
public sealed record DedupePlan(
	IReadOnlyList<DuplicateGroup> Groups,
	IReadOnlyList<string> Keepers,
	IReadOnlyList<string> Removals)
{
	public long WastedBytes { get; } = ComputeWastedBytes(Groups);

	private static long ComputeWastedBytes(IReadOnlyList<DuplicateGroup> groups)
	{
		long total = 0;
		foreach (DuplicateGroup g in groups)
		{
			total += g.FileSize * (g.Files.Count - 1);
		}

		return total;
	}
}

public sealed record DedupeStats(
	int FilesScanned,
	int DuplicateGroups,
	int RedundantFiles,
	long WastedBytes,
	Dictionary<string, int> CountByExtension);

/// <summary>
/// Scans a directory tree, groups files by SHA256, and applies (or previews)
/// "shortest filename wins" deduplication.
/// </summary>
public class FileDedupeService
{
#pragma warning disable CA1822 // instance method required for DI injection
	public async Task<DedupePlan> PlanAsync(AbsoluteDirectoryPath path, CancellationToken ct = default)
#pragma warning restore CA1822
	{
		Ensure.NotNull(path);

		string root = path.ToString();
		if (!Directory.Exists(root))
		{
			AnsiConsole.MarkupLine($"[red]Error: Directory '{root.EscapeMarkup()}' does not exist.[/]");
			return new DedupePlan([], [], []);
		}

		ReadOnlyCollection<string> files = EnumerateRegularFiles(root);

		ConcurrentDictionary<string, ConcurrentBag<(string Path, long Size)>> byHash = new();

		await Parallel.ForEachAsync(
			files,
			new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
			(file, token) => HashFileIntoAsync(file, byHash, token)).ConfigureAwait(false);

		List<DuplicateGroup> groups = [];
		List<string> keepers = [];
		List<string> removals = [];

		foreach ((string hash, ConcurrentBag<(string Path, long Size)> entries) in byHash)
		{
			if (entries.Count < 2)
			{
				continue;
			}

			List<string> paths = [.. entries.Select(e => e.Path)
				.OrderBy(p => Path.GetFileName(p).Length)
				.ThenBy(p => p, StringComparer.OrdinalIgnoreCase)];

			long size = entries.First().Size;
			groups.Add(new DuplicateGroup(hash, size, [.. paths]));

			keepers.Add(paths[0]);
			removals.AddRange(paths.Skip(1));
		}

		return new DedupePlan(groups, keepers, removals);
	}

	/// <summary>
	/// Version-control metadata names the scan never touches: directories with these names are not
	/// descended into, and a <c>.git</c> file (the pointer a worktree or submodule leaves) is not listed.
	/// </summary>
	/// <remarks>
	/// Inside <c>.git</c>, every branch ref pointing at the same commit has identical content, as do
	/// the reflogs of branches created together, so scanning it deleted branches (#222).
	/// </remarks>
	private static readonly HashSet<string> VersionControlNames = new(StringComparer.OrdinalIgnoreCase)
	{
		".git",
		".svn",
		".hg",
		".bzr",
	};

	/// <summary>
	/// Lists the regular files under <paramref name="root"/>. Symbolic links and other reparse
	/// points are skipped, and directory links are not descended into: a link hashes the same as
	/// its target, so keeping it in the scan would put the only real copy of a file up for deletion.
	/// Version-control metadata (<c>.git</c>, <c>.svn</c>, <c>.hg</c>, <c>.bzr</c>) is skipped too.
	/// </summary>
	/// <param name="root">The directory to scan.</param>
	/// <returns>The full paths of the regular files found.</returns>
	public static ReadOnlyCollection<string> EnumerateRegularFiles(string root)
	{
		EnumerationOptions options = new()
		{
			RecurseSubdirectories = true,
			AttributesToSkip = FileAttributes.ReparsePoint,
			IgnoreInaccessible = true,
		};

		FileSystemEnumerable<string> walk = new(root, (ref entry) => entry.ToFullPath(), options)
		{
			ShouldIncludePredicate = (ref entry) => !entry.IsDirectory && !VersionControlNames.Contains(entry.FileName.ToString()),
			ShouldRecursePredicate = (ref entry) => !VersionControlNames.Contains(entry.FileName.ToString()),
		};

		return new([.. walk.Where(file => new FileInfo(file).LinkTarget is null)]);
	}

	/// <summary>
	/// Hashes a single file and records it under its SHA256. Files that cannot be read
	/// (locked, deleted mid-scan, access denied) are skipped rather than failing the scan.
	/// Empty files are skipped too: they all hash alike, but they matter for existing
	/// (<c>__init__.py</c>, <c>.gitkeep</c>, marker files) and removing one saves nothing.
	/// </summary>
	private static async ValueTask HashFileIntoAsync(
		string file,
		ConcurrentDictionary<string, ConcurrentBag<(string Path, long Size)>> byHash,
		CancellationToken ct)
	{
		try
		{
			if (new FileInfo(file).Length == 0)
			{
				return;
			}

			FileStream stream = File.OpenRead(file);
			await using (stream.ConfigureAwait(false))
			{
				byte[] hashBytes = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
				string hash = Convert.ToHexString(hashBytes);
				long size = new FileInfo(file).Length;

				ConcurrentBag<(string Path, long Size)> bag = byHash.GetOrAdd(hash, _ => []);
				bag.Add((file, size));
			}
		}
		catch (IOException)
		{
			// Skip unreadable files (locked, deleted mid-scan, etc.).
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

#pragma warning disable CA1822
	public DedupeStats Summarize(DedupePlan plan, int filesScanned)
#pragma warning restore CA1822
	{
		Ensure.NotNull(plan);

		Dictionary<string, int> byExt = new(StringComparer.OrdinalIgnoreCase);
		int redundant = 0;

		foreach (DuplicateGroup group in plan.Groups)
		{
			redundant += group.Files.Count - 1;
			foreach (string file in group.Files)
			{
				string ext = Path.GetExtension(file);
				if (string.IsNullOrEmpty(ext))
				{
					ext = "(none)";
				}

				byExt[ext] = byExt.TryGetValue(ext, out int c) ? c + 1 : 1;
			}
		}

		return new DedupeStats(filesScanned, plan.Groups.Count, redundant, plan.WastedBytes, byExt);
	}

#pragma warning disable CA1822
	public int DeleteRedundant(DedupePlan plan)
#pragma warning restore CA1822
	{
		Ensure.NotNull(plan);

		int deleted = 0;
		foreach (string path in plan.Removals)
		{
			try
			{
				File.Delete(path);
				deleted++;
			}
			catch (IOException ex)
			{
				AnsiConsole.MarkupLine($"  [yellow]skip[/] {path.EscapeMarkup()}: {ex.Message.EscapeMarkup()}");
			}
			catch (UnauthorizedAccessException ex)
			{
				AnsiConsole.MarkupLine($"  [yellow]skip[/] {path.EscapeMarkup()}: {ex.Message.EscapeMarkup()}");
			}
		}

		return deleted;
	}
}
