// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Commands;
using KtsuTools.Core.Services.Git;
using KtsuTools.Core.Services.Process;
using KtsuTools.Core.Services.Settings;
using KtsuTools.Repo;
using Moq;
using Spectre.Console;
using Spectre.Console.Cli;

[TestClass]
public class RepoListCommandTests
{
	[TestMethod]
	public void SettingsDefaultToATableReadFromTheCache()
	{
		RepoListCommand.Settings settings = new();

		Assert.AreEqual("table", settings.Format, "A bare 'repo list' should render a table.");
		Assert.IsFalse(settings.Refresh, "A bare 'repo list' should read the cache, not walk the filesystem.");
	}

	[TestMethod]
	public void SettingsAcceptTheSupportedFormats()
	{
		foreach (string format in new[] { "table", "json", "JSON", "Table" })
		{
			RepoListCommand.Settings settings = new() { Format = format };

			Assert.IsTrue(
				settings.Validate().Successful,
				$"'{format}' should be accepted, case-insensitively.");
			Assert.IsTrue(
				Enum.TryParse<RepoListFormat>(format, ignoreCase: true, out _),
				$"'{format}' should map onto a RepoListFormat.");
		}
	}

	[TestMethod]
	public void SettingsRejectAnUnknownFormatBeforeAnythingRuns()
	{
		RepoListCommand.Settings settings = new() { Format = "xml" };

		ValidationResult result = settings.Validate();

		Assert.IsFalse(result.Successful, "An unsupported format should fail validation rather than fall back silently.");
		StringAssert.Contains(result.Message ?? string.Empty, "xml", "The message should name the format that was rejected.");
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ExecutingWithJsonFormatWritesTheCachedListingToStdout()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_cmd_list_{Guid.NewGuid():N}");
		string repo = Path.Join(root, "repo-a");
		string solution = Path.Join(repo, "A.sln");
		Directory.CreateDirectory(repo);

		try
		{
			using RepoCacheSettings cache = new()
			{
				Repositories = [repo],
				Solutions = [solution],
			};

			Mock<ISettingsService> settings = new();
			settings.Setup(s => s.LoadOrCreate<RepoCacheSettings>()).Returns(cache);

			RepoService service = new(new Mock<IGitService>().Object, new Mock<IProcessService>().Object, settings.Object);
			ICommand<RepoListCommand.Settings> command = new RepoListCommand(service);
			CommandContext context = new([], new NoRemainingArguments(), "list", data: null);

			using StringWriter writer = new();
			TextWriter originalOut = Console.Out;
			int exit;

			try
			{
				Console.SetOut(writer);
				exit = await command.ExecuteAsync(context, new RepoListCommand.Settings { Path = root, Format = "json" }, CancellationToken.None).ConfigureAwait(false);
			}
			finally
			{
				Console.SetOut(originalOut);
			}

			Assert.AreEqual(0, exit);

			using JsonDocument document = JsonDocument.Parse(writer.ToString());
			JsonElement repositories = document.RootElement.GetProperty("repositories");

			Assert.AreEqual(1, repositories.GetArrayLength(), "The verb should list the one cached repository.");
			Assert.AreEqual("repo-a", repositories[0].GetProperty("name").GetString());
			settings.Verify(
				s => s.SaveAsync(It.IsAny<RepoCacheSettings>()),
				Times.Never,
				"A plain 'repo list' reads the cache, so it should not rewrite it.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}
}
