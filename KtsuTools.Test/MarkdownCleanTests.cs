// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Semantics.Paths;
using KtsuTools.Markdown;

/// <summary>
/// Covers what <c>markdown clean</c> does to a file's bytes when the user has opted out of linting
/// and line-ending normalization.
/// </summary>
[TestClass]
public class MarkdownCleanTests
{
	[TestMethod]
	[DataRow("# Title\r\n\r\nSome text.\r\n", DisplayName = "CRLF")]
	[DataRow("# Title\n\nSome text.\n", DisplayName = "LF")]
	[DataRow("    indented code\n\nText.\n", DisplayName = "Leading indent")]
	[DataRow("# No final newline", DisplayName = "No final newline")]
	public async Task CleanWithoutNormalizingLeavesAFileWithNoFrontmatterUntouched(string content)
	{
		using TempDirectory dir = new();
		string file = dir.Write("README.md", content);

		int modified = await CleanAsync(dir.Root, standardizeLineEndings: false).ConfigureAwait(false);

		Assert.AreEqual(0, modified, "A file nothing was asked to change must not be counted as modified.");
		Assert.AreEqual(content, await File.ReadAllTextAsync(file).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task CleanWithoutNormalizingKeepsCrlfWhenItRewritesFrontmatter()
	{
		using TempDirectory dir = new();
		string file = dir.Write("README.md", "---\r\ntitle: Hello\r\n---\r\n# Title\r\n");

		await CleanAsync(dir.Root, standardizeLineEndings: false).ConfigureAwait(false);

		string result = await File.ReadAllTextAsync(file).ConfigureAwait(false);
		Assert.IsFalse(result.Replace("\r\n", string.Empty, StringComparison.Ordinal).Contains('\n', StringComparison.Ordinal), $"Mixed line endings in: {result}");
		Assert.IsTrue(result.EndsWith("\r\n", StringComparison.Ordinal));
	}

	[TestMethod]
	public async Task CleanWithNormalizingKeepsLeadingIndentation()
	{
		using TempDirectory dir = new();
		string file = dir.Write("README.md", "    indented code\r\n\r\nText.\r\n\r\n");

		await CleanAsync(dir.Root, standardizeLineEndings: true).ConfigureAwait(false);

		string result = await File.ReadAllTextAsync(file).ConfigureAwait(false);
		Assert.IsTrue(result.StartsWith("    indented code", StringComparison.Ordinal), $"Leading indentation lost: {result}");
		Assert.AreEqual($"    indented code{Environment.NewLine}{Environment.NewLine}Text.{Environment.NewLine}", result);
	}

	private static Task<int> CleanAsync(string root, bool standardizeLineEndings) =>
		new MarkdownService().CleanAsync(
			AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root),
			applyLinting: false,
			standardizeLineEndings,
			CancellationToken.None);

	private sealed class TempDirectory : IDisposable
	{
		public TempDirectory()
		{
			Root = Path.Join(Path.GetTempPath(), $"ktsu_mdclean_{Guid.NewGuid():N}");
			Directory.CreateDirectory(Root);
		}

		public string Root { get; }

		public string Write(string name, string content)
		{
			string path = Path.Join(Root, name);
			File.WriteAllText(path, content, new UTF8Encoding(false));
			return path;
		}

		public void Dispose()
		{
			try
			{
				Directory.Delete(Root, recursive: true);
			}
			catch (DirectoryNotFoundException)
			{
				// Already gone.
			}
		}
	}
}
