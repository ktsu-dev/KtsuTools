// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools;

using KtsuTools.BuildMonitor;
using KtsuTools.CodeGen;
using KtsuTools.Commands;
using KtsuTools.Core.Services;
using KtsuTools.FileExplorer;
using KtsuTools.Infrastructure;
using KtsuTools.Machine;
using KtsuTools.Markdown;
using KtsuTools.MemFrag;
using KtsuTools.Merge;
using KtsuTools.Packages;
using KtsuTools.Project;
using KtsuTools.Repo;
using KtsuTools.SvnMigrate;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

internal static class Program
{
	private static int Main(string[] args)
	{
		ServiceCollection services = new();
		services.AddCoreServices();

		// Feature services
		services.AddSingleton<MarkdownService>();
		services.AddSingleton<MergeService>();
		services.AddSingleton<PackagesService>();
		services.AddSingleton<SvnMigrateService>();
		services.AddSingleton<FileExplorerService>();
		services.AddSingleton<RepoService>();
		services.AddSingleton<CodeGenService>();
		services.AddSingleton<BuildMonitorService>();
		services.AddSingleton<MemFragService>();
		services.AddSingleton<MachineMonitorService>();
		services.AddSingleton<ProjectService>();

		TypeRegistrar registrar = new(services);
		CommandApp app = new(registrar);

		app.Configure(config =>
		{
			config.SetApplicationName("ktsu");
			config.SetApplicationVersion("0.1.0");

			config.AddCommand<BuildMonitorCommand>("build-monitor")
				.WithDescription("Monitor CI/CD builds from GitHub Actions and Azure DevOps")
				.WithExample("build-monitor", "--owner", "ktsu-dev");

			config.AddCommand<MergeCommand>("merge")
				.WithDescription("N-way iterative file merge with interactive conflict resolution")
				.WithExample("merge", "./repos", "*.yml");

			config.AddCommand<CodeGenCommand>("codegen")
				.WithDescription("Generate code from AST/YAML definitions")
				.WithExample("codegen", "--input", "ast.yaml", "--lang", "python");

			config.AddBranch("repo", repo =>
			{
				repo.SetDescription("Batch operations across multiple repositories");

				repo.AddCommand<RepoDiscoverCommand>("discover")
					.WithDescription("Discover git repositories in a directory")
					.WithExample("repo", "discover", "--path", "c:/dev/ktsu-dev");

				repo.AddCommand<RepoBuildCommand>("build")
					.WithDescription("Build and test all discovered solutions")
					.WithExample("repo", "build");

				repo.AddCommand<RepoPullCommand>("pull")
					.WithDescription("Git pull all discovered repositories")
					.WithExample("repo", "pull");

				repo.AddCommand<RepoUpdatePackagesCommand>("update-packages")
					.WithDescription("Update NuGet packages across all repositories")
					.WithExample("repo", "update-packages");
			});

			config.AddBranch("packages", pkg =>
			{
				pkg.SetDescription("Package and SDK management");

				pkg.AddCommand<PackagesUpdateCommand>("update")
					.WithDescription("Update .NET packages and SDKs")
					.WithExample("packages", "update", "--path", ".");

				pkg.AddCommand<PackagesMigrateCpmCommand>("migrate-cpm")
					.WithDescription("Convert to Central Package Management")
					.WithExample("packages", "migrate-cpm", "--path", ".");
			});

			config.AddBranch("markdown", md =>
			{
				md.SetDescription("Markdown processing and linting");

				md.AddCommand<MarkdownCleanCommand>("clean")
					.WithDescription("Clean and normalize markdown files")
					.WithExample("markdown", "clean", "--path", ".");

				md.AddCommand<MarkdownLintCommand>("lint")
					.WithDescription("Lint markdown files for issues")
					.WithExample("markdown", "lint", "--path", ".");
			});

			config.AddCommand<ImageProcessCommand>("image")
				.WithDescription("Batch image processing (crop, recolor, resize)")
				.WithExample("image", "--input", "./icons", "--size", "64");

			config.AddBranch("memfrag", mem =>
			{
				mem.SetDescription("Memory fragmentation analysis");

				mem.AddCommand<MemFragScanCommand>("scan")
					.WithDescription("Scan a process for memory fragmentation")
					.WithExample("memfrag", "scan", "--pid", "1234");

				mem.AddCommand<MemFragMonitorCommand>("monitor")
					.WithDescription("Monitor memory fragmentation in real-time")
					.WithExample("memfrag", "monitor", "--pid", "1234");
			});

			config.AddCommand<MachineMonitorCommand>("machine-monitor")
				.WithDescription("Real-time hardware monitoring dashboard")
				.WithExample("machine-monitor");

			config.AddCommand<ProjectCommand>("project")
				.WithDescription("GitHub repository project management")
				.WithExample("project", "--owner", "ktsu-dev");

			config.AddCommand<ExplorerCommand>("explorer")
				.WithDescription("TUI file explorer")
				.WithExample("explorer", "--path", ".");

			config.AddCommand<SvnMigrateCommand>("svn-migrate")
				.WithDescription("Migrate SVN repository to Git")
				.WithExample("svn-migrate", "--svn-url", "svn://...", "--target", "./repo");

			config.AddCommand<SyncCommand>("sync")
				.WithDescription("Synchronize file contents across repositories")
				.WithExample("sync", "--path", "c:/dev/ktsu-dev", "--filename", ".editorconfig");
		});

		return app.Run(args);
	}
}
