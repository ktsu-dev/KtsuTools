// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Repo;

using System.Text.Json;

/// <summary>
/// A top-level package reference that has a newer version available.
/// </summary>
/// <param name="Id">The package id.</param>
/// <param name="LatestVersion">The version to update to.</param>
public readonly record struct OutdatedPackage(string Id, string LatestVersion)
{
	/// <summary>
	/// Parses the output of <c>dotnet list package --outdated --format json</c>.
	/// </summary>
	/// <param name="output">The command's stdout lines.</param>
	/// <param name="packages">
	/// The outdated top-level packages, one per id. A package whose latest version could not be
	/// resolved is left out, since there is nothing to update it to.
	/// </param>
	/// <returns>
	/// <see langword="false"/> when the output is not the expected JSON or reports an error,
	/// so the caller can tell "nothing to update" apart from "could not tell".
	/// </returns>
	public static bool TryParse(IReadOnlyList<string>? output, out IReadOnlyList<OutdatedPackage> packages)
	{
		packages = [];

		string json = string.Join('\n', output ?? []);
		int start = json.IndexOf('{', StringComparison.Ordinal);

		if (start < 0)
		{
			return false;
		}

		List<OutdatedPackage> found = [];

		try
		{
			using JsonDocument document = JsonDocument.Parse(json[start..]);
			JsonElement root = document.RootElement;

			if (root.TryGetProperty("problems", out JsonElement problems) &&
				problems.ValueKind == JsonValueKind.Array &&
				problems.EnumerateArray().Any(p => string.Equals(GetString(p, "level"), "error", StringComparison.OrdinalIgnoreCase)))
			{
				return false;
			}

			if (!root.TryGetProperty("projects", out JsonElement projects) || projects.ValueKind != JsonValueKind.Array)
			{
				return false;
			}

			foreach (JsonElement package in projects.EnumerateArray()
				.SelectMany(project => GetArray(project, "frameworks"))
				.SelectMany(framework => GetArray(framework, "topLevelPackages")))
			{
				string? id = GetString(package, "id");
				string? latest = GetString(package, "latestVersion");

				// 'latestVersion' is "Not found at the sources" when no feed knows the package.
				bool hasLatest = !string.IsNullOrEmpty(latest) && char.IsAsciiDigit(latest[0]);

				if (string.IsNullOrEmpty(id) || !hasLatest ||
					string.Equals(latest, GetString(package, "resolvedVersion"), StringComparison.OrdinalIgnoreCase) ||
					found.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
				{
					continue;
				}

				found.Add(new OutdatedPackage(id, latest!));
			}
		}
		catch (JsonException)
		{
			return false;
		}

		packages = found;
		return true;
	}

	private static JsonElement[] GetArray(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object &&
		element.TryGetProperty(name, out JsonElement value) &&
		value.ValueKind == JsonValueKind.Array
			? [.. value.EnumerateArray()]
			: [];

	private static string? GetString(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object &&
		element.TryGetProperty(name, out JsonElement value) &&
		value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}
