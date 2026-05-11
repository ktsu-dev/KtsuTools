// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Merge;

using System;

/// <summary>
/// Maps the string form of <see cref="DiffStyle"/> used in CLI flags and persisted batch
/// configs (e.g. "side-by-side", "git") to the enum and back.
/// </summary>
public static class DiffStyleParser
{
	public const string SideBySideName = "side-by-side";
	public const string GitName = "git";

	public static bool TryParse(string? value, out DiffStyle style)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			style = DiffStyle.SideBySide;
			return true;
		}

		switch (value.Trim().ToLowerInvariant())
		{
			case SideBySideName:
			case "sidebyside":
			case "side":
				style = DiffStyle.SideBySide;
				return true;
			case GitName:
			case "unified":
				style = DiffStyle.Git;
				return true;
			default:
				style = DiffStyle.SideBySide;
				return false;
		}
	}

	public static string ToCanonicalString(DiffStyle style) => style switch
	{
		DiffStyle.Git => GitName,
		_ => SideBySideName,
	};
}
