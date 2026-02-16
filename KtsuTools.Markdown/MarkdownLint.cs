// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Markdown;

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static partial class MarkdownLint
{
	private static readonly ConcurrentDictionary<string, string?> ConfigPathCache = new();
	private static readonly ConcurrentDictionary<string, Dictionary<string, JsonElement>> ParsedConfigCache = new();
	private static readonly string[] ConfigFileNames = [".markdownlint.json", ".markdownlint.jsonc"];

	internal static string? FindNearestConfigPath(string filePath)
	{
		string? directory = Path.GetDirectoryName(filePath);
		if (string.IsNullOrEmpty(directory))
		{
			return null;
		}

		if (ConfigPathCache.TryGetValue(directory, out string? cached))
		{
			return cached;
		}

		string? currentDir = directory;
		while (!string.IsNullOrEmpty(currentDir))
		{
			foreach (string configFileName in ConfigFileNames)
			{
				string configPath = Path.Combine(currentDir, configFileName);
				if (File.Exists(configPath))
				{
					ConfigPathCache[directory] = configPath;
					return configPath;
				}

				string githubConfigPath = Path.Combine(currentDir, ".github", configFileName);
				if (File.Exists(githubConfigPath))
				{
					ConfigPathCache[directory] = githubConfigPath;
					return githubConfigPath;
				}
			}

			DirectoryInfo? parent = Directory.GetParent(currentDir);
			currentDir = parent?.FullName;
		}

		ConfigPathCache[directory] = null;
		return null;
	}

	internal static bool TryParseMarkdownLintConfig(string configPath, out Dictionary<string, JsonElement>? config)
	{
		if (ParsedConfigCache.TryGetValue(configPath, out config))
		{
			return true;
		}

		try
		{
			string content = File.ReadAllText(configPath);

			if (configPath.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase))
			{
				content = RemoveLineCommentsRegex().Replace(content, "$1");
				content = RemoveBlockCommentsRegex().Replace(content, string.Empty);
			}

			config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content);
			if (config is not null)
			{
				ParsedConfigCache[configPath] = config;
				return true;
			}
		}
		catch (JsonException)
		{
		}
		catch (IOException)
		{
		}

		config = null;
		return false;
	}

	internal static string FormatMarkdown(string content, string? configPath)
	{
		Dictionary<string, JsonElement>? config = null;
		if (configPath is not null)
		{
			_ = TryParseMarkdownLintConfig(configPath, out config);
		}

		string result = content;
		result = FixLineEndings(result);
		result = FixConsecutiveBlankLines(result, config);
		result = FixHeadingSpacing(result, config);
		result = FixListMarkers(result, config);
		result = FixTrailingWhitespace(result, config);
		result = EnsureFinalNewline(result);
		return result;
	}

	private static string FixLineEndings(string content) => content.ReplaceLineEndings(Environment.NewLine);

	private static string FixConsecutiveBlankLines(string content, Dictionary<string, JsonElement>? config)
	{
		if (!IsRuleEnabled(config, "MD012"))
		{
			return content;
		}

		int maximum = 1;
		if (config is not null && config.TryGetValue("MD012", out JsonElement md012))
		{
			if (md012.ValueKind == JsonValueKind.Object && md012.TryGetProperty("maximum", out JsonElement maxProp))
			{
				maximum = maxProp.GetInt32();
			}
		}

		string[] lines = content.Split(Environment.NewLine);
		List<string> result = [];
		int consecutiveBlank = 0;

		foreach (string line in lines)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				consecutiveBlank++;
				if (consecutiveBlank <= maximum)
				{
					result.Add(line);
				}
			}
			else
			{
				consecutiveBlank = 0;
				result.Add(line);
			}
		}

		return string.Join(Environment.NewLine, result);
	}

	private static string FixHeadingSpacing(string content, Dictionary<string, JsonElement>? config)
	{
		if (!IsRuleEnabled(config, "MD018") && !IsRuleEnabled(config, "MD019"))
		{
			return content;
		}

		string[] lines = content.Split(Environment.NewLine);
		for (int i = 0; i < lines.Length; i++)
		{
			string trimmed = lines[i].TrimStart();
			if (trimmed.StartsWith('#'))
			{
				int hashCount = 0;
				while (hashCount < trimmed.Length && trimmed[hashCount] == '#')
				{
					hashCount++;
				}

				if (hashCount is >= 1 and <= 6)
				{
					string rest = trimmed[hashCount..].TrimStart();
					if (!string.IsNullOrEmpty(rest))
					{
						string leading = lines[i][..(lines[i].Length - trimmed.Length)];
						lines[i] = $"{leading}{new string('#', hashCount)} {rest}";
					}
				}
			}
		}

		return string.Join(Environment.NewLine, lines);
	}

	private static string FixListMarkers(string content, Dictionary<string, JsonElement>? config)
	{
		if (!IsRuleEnabled(config, "MD004"))
		{
			return content;
		}

		string style = "dash";
		int indent = 2;

		if (config is not null)
		{
			if (config.TryGetValue("MD004", out JsonElement md004) && md004.ValueKind == JsonValueKind.Object)
			{
				if (md004.TryGetProperty("style", out JsonElement styleProp))
				{
					style = styleProp.GetString() ?? "dash";
				}
			}

			if (config.TryGetValue("MD007", out JsonElement md007) && md007.ValueKind == JsonValueKind.Object)
			{
				if (md007.TryGetProperty("indent", out JsonElement indentProp))
				{
					indent = indentProp.GetInt32();
				}
			}
		}

		char marker = style switch
		{
			"asterisk" => '*',
			"plus" => '+',
			_ => '-',
		};

		string[] lines = content.Split(Environment.NewLine);
		for (int i = 0; i < lines.Length; i++)
		{
			string trimmed = lines[i].TrimStart();
			if (trimmed.Length > 1 && trimmed[1] == ' ' && (trimmed[0] is '*' or '+' or '-'))
			{
				int leadingSpaces = lines[i].Length - trimmed.Length;
				int level = leadingSpaces / indent;
				string newIndent = new(' ', level * indent);
				lines[i] = $"{newIndent}{marker} {trimmed[2..]}";
			}
		}

		return string.Join(Environment.NewLine, lines);
	}

	private static string FixTrailingWhitespace(string content, Dictionary<string, JsonElement>? config)
	{
		if (!IsRuleEnabled(config, "MD009"))
		{
			return content;
		}

		int brSpaces = 0;
		if (config is not null && config.TryGetValue("MD009", out JsonElement md009) && md009.ValueKind == JsonValueKind.Object)
		{
			if (md009.TryGetProperty("br_spaces", out JsonElement brProp))
			{
				if (brProp.ValueKind == JsonValueKind.Number)
				{
					brSpaces = brProp.GetInt32();
				}
				else if (brProp.ValueKind == JsonValueKind.True)
				{
					brSpaces = 2;
				}
			}
		}

		string[] lines = content.Split(Environment.NewLine);
		for (int i = 0; i < lines.Length; i++)
		{
			string trimmed = lines[i].TrimEnd();
			if (brSpaces > 0)
			{
				int trailingCount = lines[i].Length - trimmed.Length;
				if (trailingCount == brSpaces)
				{
					continue;
				}
			}

			lines[i] = trimmed;
		}

		return string.Join(Environment.NewLine, lines);
	}

	private static string EnsureFinalNewline(string content)
	{
		if (!content.EndsWith(Environment.NewLine, StringComparison.Ordinal))
		{
			content += Environment.NewLine;
		}

		return content;
	}

	private static bool IsRuleEnabled(Dictionary<string, JsonElement>? config, string ruleName)
	{
		if (config is null)
		{
			return true;
		}

		if (config.TryGetValue(ruleName, out JsonElement ruleValue))
		{
			if (ruleValue.ValueKind == JsonValueKind.False)
			{
				return false;
			}

			return true;
		}

		if (config.TryGetValue("default", out JsonElement defaultValue))
		{
			return defaultValue.ValueKind != JsonValueKind.False;
		}

		return true;
	}

	[GeneratedRegex(@"//.*?(\r?\n|$)")]
	private static partial Regex RemoveLineCommentsRegex();

	[GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
	private static partial Regex RemoveBlockCommentsRegex();
}
