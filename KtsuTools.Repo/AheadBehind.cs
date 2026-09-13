// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Repo;

using System.Globalization;

/// <summary>
/// How far a branch has diverged from its upstream.
/// </summary>
/// <param name="Ahead">Commits the branch has that its upstream does not.</param>
/// <param name="Behind">Commits the upstream has that the branch does not.</param>
public readonly record struct AheadBehind(int Ahead, int Behind)
{
	/// <summary>Rendered for a repository with no upstream to compare against.</summary>
	public const string NoUpstream = "—";

	/// <summary>Rendered for a branch sitting on the same commit as its upstream.</summary>
	public const string InSync = "≡";

	/// <summary>
	/// Parses the output of <c>git rev-list --left-right --count HEAD...@{upstream}</c>, a single
	/// line of two tab-separated counts.
	/// </summary>
	/// <param name="output">The command's stdout lines.</param>
	/// <returns>
	/// The counts, or <see langword="null"/> when the output is not that line — which is what a
	/// repository with no upstream, no commits, or a detached HEAD produces.
	/// </returns>
	public static AheadBehind? Parse(IReadOnlyList<string>? output)
	{
		string? line = output?.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));

		if (line is null)
		{
			return null;
		}

		string[] counts = line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);

		return counts.Length == 2 &&
			int.TryParse(counts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int ahead) &&
			int.TryParse(counts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int behind)
			? new AheadBehind(ahead, behind)
			: null;
	}

	/// <summary>
	/// Renders divergence as <c>↑n ↓n</c>, dropping whichever side is zero.
	/// </summary>
	/// <param name="divergence">The counts, or <see langword="null"/> when there is no upstream.</param>
	/// <returns>
	/// <see cref="NoUpstream"/> when <paramref name="divergence"/> is <see langword="null"/>,
	/// <see cref="InSync"/> when both counts are zero, otherwise the arrows.
	/// </returns>
	public static string Render(AheadBehind? divergence)
	{
		if (divergence is not AheadBehind counts)
		{
			return NoUpstream;
		}

		if (counts is { Ahead: 0, Behind: 0 })
		{
			return InSync;
		}

		string ahead = counts.Ahead > 0 ? $"↑{counts.Ahead.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
		string behind = counts.Behind > 0 ? $"↓{counts.Behind.ToString(CultureInfo.InvariantCulture)}" : string.Empty;

		return string.Join(' ', new[] { ahead, behind }.Where(part => part.Length > 0));
	}
}
