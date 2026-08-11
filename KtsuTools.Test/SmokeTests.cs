// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Test;

using KtsuTools.Core.Services.Git;
using KtsuTools.Core.Services.GitHub;
using KtsuTools.Core.Services.Process;

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
		Assert.IsNotNull(typeof(Sync.SyncService));
	}
}
