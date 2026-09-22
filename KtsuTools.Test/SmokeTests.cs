// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using ktsu.GitIntegration;

using KtsuTools.Core.Services;
using KtsuTools.Core.Services.Git;
using KtsuTools.Core.Services.GitHub;
using KtsuTools.Core.Services.Process;
using KtsuTools.Core.Services.Settings;

using Microsoft.Extensions.DependencyInjection;

using Moq;

[TestClass]
public class SmokeTests
{
	[TestMethod]
	public void CoreServicesCanBeCreated()
	{
		GitService gitService = new();
		ProcessService processService = new();
		Assert.IsNotNull(gitService);
		Assert.IsNotNull(processService);
	}

	/// <summary>
	/// Resolves what <c>AddCoreServices</c> registers. <see cref="GitService"/> takes an
	/// <see cref="IGitClient"/>, which ktsu.GitIntegration registers rather than this repository,
	/// so leaving that registration out would not fail the build — it would fail here, and
	/// otherwise only once the application started.
	/// </summary>
	[TestMethod]
	public void CoreServicesResolveThroughDependencyInjection()
	{
		ServiceCollection services = new();
		_ = services.AddCoreServices();

		using ServiceProvider provider = services.BuildServiceProvider();

		Assert.IsInstanceOfType<GitService>(provider.GetRequiredService<IGitService>());
		Assert.IsNotNull(provider.GetRequiredService<IGitHubService>());
		Assert.IsNotNull(provider.GetRequiredService<IProcessService>());
		Assert.IsNotNull(provider.GetRequiredService<ISettingsService>());
		Assert.IsNotNull(provider.GetRequiredService<IGitClient>());
	}

	[TestMethod]
	public void FeatureServicesCanBeCreated()
	{
		IGitHubService mockGitHubService = new Mock<IGitHubService>().Object;
		IGitService mockGitService = new Mock<IGitService>().Object;
		IProcessService mockProcessService = new Mock<IProcessService>().Object;

		Assert.IsNotNull(new BuildMonitor.BuildMonitorService(mockGitHubService));
		Assert.IsNotNull(new Merge.MergeService());
		Assert.IsNotNull(new CodeGen.CodeGenService());
		Assert.IsNotNull(new Repo.RepoService(mockGitService, mockProcessService));
		Assert.IsNotNull(new Packages.PackagesService(mockProcessService));
		Assert.IsNotNull(new Markdown.MarkdownService());
		Assert.IsNotNull(typeof(Image.ImageService));
		Assert.IsNotNull(new MemFrag.MemFragService());
		Assert.IsNotNull(new Machine.MachineMonitorService());
		Assert.IsNotNull(new Project.ProjectService(mockGitService, mockGitHubService));
		Assert.IsNotNull(new FileExplorer.FileExplorerService());
		Assert.IsNotNull(new SvnMigrate.SvnMigrateService(mockProcessService));
		Assert.IsNotNull(new Sync.SyncService(mockProcessService));
	}
}
