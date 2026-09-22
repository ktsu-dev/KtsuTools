// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using KtsuTools.Core.Services.Settings;
using KtsuTools.Sync;
using Moq;

[TestClass]
public class SyncConfigServiceTests
{
	private static (SyncConfigService Service, SyncConfigSettings Store, Mock<ISettingsService> SettingsMock) BuildService()
	{
		SyncConfigSettings store = new();
		Mock<ISettingsService> settings = new();
		settings.Setup(s => s.LoadOrCreate<SyncConfigSettings>()).Returns(store);
		settings.Setup(s => s.SaveAsync(It.IsAny<SyncConfigSettings>())).Returns(Task.CompletedTask);
		SyncConfigService service = new(settings.Object);
		return (service, store, settings);
	}

	[TestMethod]
	public async Task SaveListShowDeleteRoundTrip()
	{
		(SyncConfigService service, SyncConfigSettings store, Mock<ISettingsService> settings) = BuildService();

		SyncConfigEntry entry = new()
		{
			Path = "/tmp/repos",
			Filenames = [".editorconfig", ".gitignore"],
			AutoPush = true,
			Branch = "sync/shared",
			OpenPullRequest = true,
		};

		await service.SaveAsync("org-shared", entry).ConfigureAwait(false);

		Assert.AreEqual(1, service.List().Count);
		Assert.IsTrue(service.List().ContainsKey("org-shared"));

		SyncConfigEntry? loaded = service.Get("org-shared");
		Assert.IsNotNull(loaded);
		Assert.AreEqual("/tmp/repos", loaded.Path);
		Assert.AreEqual(".editorconfig|.gitignore", string.Join('|', loaded.Filenames));
		Assert.IsTrue(loaded.AutoPush);
		Assert.AreEqual("sync/shared", loaded.Branch);
		Assert.IsTrue(loaded.OpenPullRequest);

		bool removed = await service.DeleteAsync("org-shared").ConfigureAwait(false);
		Assert.IsTrue(removed);
		Assert.AreEqual(0, service.List().Count);
		Assert.IsNull(service.Get("org-shared"));

		settings.Verify(s => s.SaveAsync(store), Times.Exactly(2));
	}

	[TestMethod]
	public async Task DeleteUnknownReturnsFalseAndDoesNotPersist()
	{
		(SyncConfigService service, _, Mock<ISettingsService> settings) = BuildService();

		bool removed = await service.DeleteAsync("missing").ConfigureAwait(false);

		Assert.IsFalse(removed);
		settings.Verify(s => s.SaveAsync(It.IsAny<SyncConfigSettings>()), Times.Never);
	}

	[TestMethod]
	public void GetUnknownReturnsNull()
	{
		(SyncConfigService service, _, _) = BuildService();
		Assert.IsNull(service.Get("nope"));
	}

	[TestMethod]
	public async Task SaveOverwritesExistingEntry()
	{
		(SyncConfigService service, _, _) = BuildService();

		await service.SaveAsync("name", new SyncConfigEntry { Path = "a", Filenames = ["b"] }).ConfigureAwait(false);
		await service.SaveAsync("name", new SyncConfigEntry { Path = "c", Filenames = ["d"], Branch = "sync/x" }).ConfigureAwait(false);

		SyncConfigEntry? loaded = service.Get("name");
		Assert.IsNotNull(loaded);
		Assert.AreEqual("c", loaded.Path);
		Assert.AreEqual("d", string.Join('|', loaded.Filenames));
		Assert.AreEqual("sync/x", loaded.Branch);
	}

	[TestMethod]
	public void SharesThePersistencePathWithSavedMergeBatches()
	{
		// Both stores derive from AppData<T>, so the saved configurations live alongside merge batches
		// rather than in a second, sync-specific config format.
		Assert.IsTrue(typeof(SyncConfigSettings).IsSubclassOf(typeof(ktsu.AppDataStorage.AppData<SyncConfigSettings>)));
	}
}
