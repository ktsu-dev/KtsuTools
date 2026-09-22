// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Commands;
using KtsuTools.Core.Services.GitHub;
using KtsuTools.Core.Services.Process;
using KtsuTools.Core.Services.Settings;
using KtsuTools.Sync;
using Moq;
using Spectre.Console;
using Spectre.Console.Cli;

/// <summary>
/// Drives the sync-config verbs and the paths through <c>sync</c> that a saved configuration opens.
/// </summary>
[TestClass]
public sealed class SyncConfigCommandTests : IDisposable
{
	private SyncConfigSettings store = new();
	private SyncConfigService service = null!;

	[TestInitialize]
	public void BuildService()
	{
		store = new SyncConfigSettings();
		Mock<ISettingsService> settings = new();
		settings.Setup(s => s.LoadOrCreate<SyncConfigSettings>()).Returns(store);
		settings.Setup(s => s.SaveAsync(It.IsAny<SyncConfigSettings>())).Returns(Task.CompletedTask);
		service = new SyncConfigService(settings.Object);
	}

	/// <summary>
	/// The settings store is an <c>AppData</c>, which is disposable, and it outlives a single test
	/// method because the helpers below share it.
	/// </summary>
	public void Dispose() => store.Dispose();

	private static CommandContext Context(string name) => new([], new NoRemainingArguments(), name, data: null);

	[TestMethod]
	public void SaveRejectsAConfigurationWithNoFilenames()
	{
		SyncConfigSaveCommand.Settings settings = new() { Name = "org-shared", Path = "/tmp/repos" };

		ValidationResult result = settings.Validate();

		Assert.IsFalse(result.Successful, "A configuration with no filenames would sync nothing.");
		StringAssert.Contains(result.Message ?? string.Empty, "--filename");
	}

	[TestMethod]
	public void SaveRejectsPullRequestsWithoutABranch()
	{
		SyncConfigSaveCommand.Settings settings = new()
		{
			Name = "org-shared",
			Path = "/tmp/repos",
			Filename = [".editorconfig"],
			OpenPullRequest = true,
		};

		ValidationResult result = settings.Validate();

		Assert.IsFalse(result.Successful);
		StringAssert.Contains(result.Message ?? string.Empty, "--branch");
	}

	[TestMethod]
	public void SaveAcceptsAConfigurationThatNamesFilenames()
	{
		SyncConfigSaveCommand.Settings settings = new()
		{
			Name = "org-shared",
			Path = "/tmp/repos",
			Filename = [".editorconfig,.gitignore"],
		};

		Assert.IsTrue(settings.Validate().Successful);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task SavingExpandsCommaSeparatedFilenamesAndStoresTheOptions()
	{
		ICommand<SyncConfigSaveCommand.Settings> command = new SyncConfigSaveCommand(service);

		string output = await ConsoleCapture.CaptureAsync(() => command.ExecuteAsync(
			Context("save"),
			new SyncConfigSaveCommand.Settings
			{
				Name = "org-shared",
				Path = "/tmp/repos",
				Filename = [".editorconfig, .gitignore", ".gitignore"],
				AutoPush = true,
				Branch = "sync/shared",
				OpenPullRequest = true,
			},
			CancellationToken.None)).ConfigureAwait(false);

		StringAssert.Contains(output, "org-shared");

		SyncConfigEntry? saved = service.Get("org-shared");
		Assert.IsNotNull(saved);
		Assert.AreEqual("/tmp/repos", saved.Path);
		Assert.AreEqual(".editorconfig|.gitignore", string.Join('|', saved.Filenames), "Comma-separated entries expand and duplicates drop, as they do on the sync flag.");
		Assert.IsTrue(saved.AutoPush);
		Assert.AreEqual("sync/shared", saved.Branch);
		Assert.IsTrue(saved.OpenPullRequest);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task SavingWithoutABranchRecordsTheCheckedOutBranch()
	{
		ICommand<SyncConfigSaveCommand.Settings> command = new SyncConfigSaveCommand(service);

		_ = await ConsoleCapture.CaptureAsync(() => command.ExecuteAsync(
			Context("save"),
			new SyncConfigSaveCommand.Settings { Name = "plain", Path = "/tmp/repos", Filename = [".editorconfig"] },
			CancellationToken.None)).ConfigureAwait(false);

		SyncConfigEntry? saved = service.Get("plain");
		Assert.IsNotNull(saved);
		Assert.IsNull(saved.Branch, "A blank --branch means the checked-out branch, not a branch named the empty string.");
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ListingWithNothingSavedSaysSoAndPointsAtSave()
	{
		ICommand<SyncConfigListCommand.Settings> command = new SyncConfigListCommand(service);

		string output = await ConsoleCapture.CaptureAsync(() =>
			command.ExecuteAsync(Context("list"), new SyncConfigListCommand.Settings(), CancellationToken.None)).ConfigureAwait(false);

		StringAssert.Contains(output, "No saved sync configurations");
		StringAssert.Contains(output, "sync-config save");
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ListingShowsEachSavedConfiguration()
	{
		await Save("org-shared", branch: "sync/shared").ConfigureAwait(false);

		ICommand<SyncConfigListCommand.Settings> command = new SyncConfigListCommand(service);
		string output = await ConsoleCapture.CaptureAsync(() =>
			command.ExecuteAsync(Context("list"), new SyncConfigListCommand.Settings(), CancellationToken.None)).ConfigureAwait(false);

		StringAssert.Contains(output, "org-shared");
		StringAssert.Contains(output, ".editorconfig");
		StringAssert.Contains(output, "sync/shared");
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ShowingAnUnknownConfigurationFails()
	{
		ICommand<SyncConfigShowCommand.Settings> command = new SyncConfigShowCommand(service);
		int exit = 0;

		string output = await ConsoleCapture.CaptureAsync(async () =>
			exit = await command.ExecuteAsync(Context("show"), new SyncConfigShowCommand.Settings { Name = "missing" }, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exit);
		StringAssert.Contains(output, "missing");
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ShowingReportsEveryStoredOption()
	{
		await Save("org-shared", branch: null).ConfigureAwait(false);

		ICommand<SyncConfigShowCommand.Settings> command = new SyncConfigShowCommand(service);
		int exit = 1;

		string output = await ConsoleCapture.CaptureAsync(async () =>
			exit = await command.ExecuteAsync(Context("show"), new SyncConfigShowCommand.Settings { Name = "org-shared" }, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		StringAssert.Contains(output, "org-shared");
		StringAssert.Contains(output, ".editorconfig");
		StringAssert.Contains(output, "checked out", "A configuration with no branch reports the checked-out one rather than a blank.");
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task DeletingAnUnknownConfigurationFails()
	{
		ICommand<SyncConfigDeleteCommand.Settings> command = new SyncConfigDeleteCommand(service);
		int exit = 0;

		string output = await ConsoleCapture.CaptureAsync(async () =>
			exit = await command.ExecuteAsync(Context("delete"), new SyncConfigDeleteCommand.Settings { Name = "missing" }, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exit);
		StringAssert.Contains(output, "missing");
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task DeletingRemovesTheConfiguration()
	{
		await Save("org-shared", branch: null).ConfigureAwait(false);

		ICommand<SyncConfigDeleteCommand.Settings> command = new SyncConfigDeleteCommand(service);
		int exit = 1;

		string output = await ConsoleCapture.CaptureAsync(async () =>
			exit = await command.ExecuteAsync(Context("delete"), new SyncConfigDeleteCommand.Settings { Name = "org-shared" }, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		StringAssert.Contains(output, "org-shared");
		Assert.IsNull(service.Get("org-shared"));
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task SyncingAnUnknownConfigurationFailsBeforeAnythingIsScanned()
	{
		ICommand<SyncCommand.Settings> command = new SyncCommand(SyncService(), service);
		int exit = 0;

		string output = await ConsoleCapture.CaptureAsync(async () =>
			exit = await command.ExecuteAsync(Context("sync"), new SyncCommand.Settings { ConfigName = "missing" }, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exit);
		StringAssert.Contains(output, "missing");
		StringAssert.Contains(output, "sync-config list", "The message should say where to find the saved names.");
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task SyncingAConfigurationThatAsksForPullRequestsWithoutABranchFails()
	{
		// Settings.Validate cannot reject this, because the configuration is not loaded until the
		// run starts. The check therefore has to happen against the resolved values.
		await service.SaveAsync("no-branch", new SyncConfigEntry
		{
			Path = "/tmp/repos",
			Filenames = [".editorconfig"],
			OpenPullRequest = true,
		}).ConfigureAwait(false);

		ICommand<SyncCommand.Settings> command = new SyncCommand(SyncService(), service);
		int exit = 0;

		string output = await ConsoleCapture.CaptureAsync(async () =>
			exit = await command.ExecuteAsync(Context("sync"), new SyncCommand.Settings { ConfigName = "no-branch" }, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exit);
		StringAssert.Contains(output, "--pr");
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task SyncingAConfigurationRunsWithoutTheFlagsItSupplies()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_sync_config_{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(root);

		try
		{
			await service.SaveAsync("org-shared", new SyncConfigEntry
			{
				Path = root,
				Filenames = [".editorconfig"],
			}).ConfigureAwait(false);

			ICommand<SyncCommand.Settings> command = new SyncCommand(SyncService(), service);
			int exit = 1;

			string output = await ConsoleCapture.CaptureAsync(async () =>
				exit = await command.ExecuteAsync(Context("sync"), new SyncCommand.Settings { ConfigName = "org-shared" }, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

			// The run reaches the scan, which means the saved path and filenames were used: neither
			// was given as a flag, and without them the command would have stopped to prompt.
			Assert.AreEqual(0, exit);
			StringAssert.Contains(output, "org-shared");
			StringAssert.Contains(output, ".editorconfig");
			StringAssert.Contains(output, root);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task AnExplicitFilenameOverridesTheSavedOne()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_sync_override_{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(root);

		try
		{
			await service.SaveAsync("org-shared", new SyncConfigEntry
			{
				Path = root,
				Filenames = [".editorconfig"],
			}).ConfigureAwait(false);

			ICommand<SyncCommand.Settings> command = new SyncCommand(SyncService(), service);

			string output = await ConsoleCapture.CaptureAsync(() => command.ExecuteAsync(
				Context("sync"),
				new SyncCommand.Settings { ConfigName = "org-shared", Filename = ["Directory.Build.props"] },
				CancellationToken.None)).ConfigureAwait(false);

			StringAssert.Contains(output, "Directory.Build.props");
			Assert.DoesNotContain(".editorconfig", output, "The flag replaces the saved list rather than adding to it.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	private static SyncService SyncService() =>
		new(new Mock<IProcessService>().Object, new Mock<IGitHubService>().Object);

	private async Task Save(string name, string? branch) =>
		await service.SaveAsync(name, new SyncConfigEntry
		{
			Path = "/tmp/repos",
			Filenames = [".editorconfig"],
			Branch = branch,
		}).ConfigureAwait(false);
}
