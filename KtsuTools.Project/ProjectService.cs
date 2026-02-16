// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Project;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using KtsuTools.Core.Services.Git;
using KtsuTools.Core.Services.GitHub;
using Spectre.Console;

public class ProjectService(IGitService gitService, IGitHubService gitHubService)
{
	public async Task<int> RunAsync(string owner, CancellationToken ct = default)
	{
		if (!gitHubService.IsAuthenticated)
		{
			string token = AnsiConsole.Prompt(
				new TextPrompt<string>("Enter GitHub personal access token:")
					.Secret());
			await gitHubService.InitializeAsync(token, ct).ConfigureAwait(false);
		}

		AnsiConsole.MarkupLine($"[bold blue]Project Manager[/] - {owner.EscapeMarkup()}");

		string currentDir = Directory.GetCurrentDirectory();
		bool isLocalRepo = await gitService.IsRepositoryAsync(currentDir, ct).ConfigureAwait(false);
		if (isLocalRepo)
		{
			string branch = await gitService.GetCurrentBranchAsync(currentDir, ct).ConfigureAwait(false);
			AnsiConsole.MarkupLine($"[dim]Local repo: {currentDir.EscapeMarkup()} (branch: {branch.EscapeMarkup()})[/]");
		}

		AnsiConsole.WriteLine();

		IReadOnlyList<GitHubRepository> repos = await AnsiConsole.Status()
			.Spinner(Spinner.Known.Dots)
			.StartAsync("Fetching repositories...", async _ =>
				await gitHubService.GetRepositoriesAsync(owner, ct).ConfigureAwait(false))
			.ConfigureAwait(false);

		if (repos.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No repositories found.[/]");
			return 0;
		}

		while (!ct.IsCancellationRequested)
		{
			AnsiConsole.MarkupLine($"\n[bold]Repositories ({repos.Count})[/]");

			List<string> choices = [.. repos.Select(r => r.Name), "Exit"];

			string selection = AnsiConsole.Prompt(
				new SelectionPrompt<string>()
					.Title("Select a repository:")
					.PageSize(20)
					.AddChoices(choices));

			if (string.Equals(selection, "Exit", StringComparison.Ordinal))
			{
				break;
			}

			GitHubRepository? selectedRepo = repos.FirstOrDefault(r =>
				string.Equals(r.Name, selection, StringComparison.Ordinal));

			if (selectedRepo is null)
			{
				continue;
			}

			await ShowRepositoryDetailsAsync(owner, selectedRepo, ct).ConfigureAwait(false);
		}

		return 0;
	}

	private async Task ShowRepositoryDetailsAsync(string owner, GitHubRepository repo, CancellationToken ct)
	{
		Table infoTable = new()
		{
			Border = TableBorder.Rounded,
			Title = new TableTitle($"[bold]{repo.Name.EscapeMarkup()}[/]"),
		};

		infoTable.AddColumn("Property");
		infoTable.AddColumn("Value");

		infoTable.AddRow("Full Name", repo.FullName.EscapeMarkup());
		infoTable.AddRow("Default Branch", repo.DefaultBranch.EscapeMarkup());
		infoTable.AddRow("URL", repo.Url.ToString().EscapeMarkup());

		AnsiConsole.Write(infoTable);
		AnsiConsole.WriteLine();

		IReadOnlyList<GitHubWorkflowRun> runs = await AnsiConsole.Status()
			.Spinner(Spinner.Known.Dots)
			.StartAsync("Fetching workflow runs...", async _ =>
				await gitHubService.GetWorkflowRunsAsync(owner, repo.Name, ct).ConfigureAwait(false))
			.ConfigureAwait(false);

		if (runs.Count > 0)
		{
			Table runsTable = new()
			{
				Border = TableBorder.Rounded,
				Title = new TableTitle("[bold]Recent Workflow Runs[/]"),
			};

			runsTable.AddColumn("Workflow");
			runsTable.AddColumn("Status");
			runsTable.AddColumn("Branch");
			runsTable.AddColumn("Created");

			foreach (GitHubWorkflowRun run in runs.Take(10))
			{
				string statusMarkup = FormatRunStatus(run.Status, run.Conclusion);
				runsTable.AddRow(
					run.Name.EscapeMarkup(),
					statusMarkup,
					run.Branch.EscapeMarkup(),
					run.CreatedAt.ToString("g", CultureInfo.InvariantCulture).EscapeMarkup());
			}

			AnsiConsole.Write(runsTable);
		}
		else
		{
			AnsiConsole.MarkupLine("[dim]No workflow runs found.[/]");
		}

		string action = AnsiConsole.Prompt(
			new SelectionPrompt<string>()
				.Title("Actions:")
				.AddChoices("Rerun Latest Workflow", "Cancel Running Workflow", "Compare Files", "Back"));

		switch (action)
		{
			case "Rerun Latest Workflow" when runs.Count > 0:
				bool rerunResult = await gitHubService.RerunWorkflowAsync(owner, repo.Name, runs[0].Id, ct).ConfigureAwait(false);
				AnsiConsole.MarkupLine(rerunResult
					? "[green]Workflow rerun triggered successfully.[/]"
					: "[red]Failed to trigger workflow rerun.[/]");
				break;

			case "Cancel Running Workflow":
				GitHubWorkflowRun? runningWorkflow = runs.FirstOrDefault(r =>
					string.Equals(r.Status, "in_progress", StringComparison.OrdinalIgnoreCase));
				if (runningWorkflow is not null)
				{
					bool cancelResult = await gitHubService.CancelWorkflowAsync(owner, repo.Name, runningWorkflow.Id, ct).ConfigureAwait(false);
					AnsiConsole.MarkupLine(cancelResult
						? "[green]Workflow cancelled successfully.[/]"
						: "[red]Failed to cancel workflow.[/]");
				}
				else
				{
					AnsiConsole.MarkupLine("[yellow]No running workflow found.[/]");
				}

				break;

			case "Compare Files":
				await CompareLocalFilesAsync(ct).ConfigureAwait(false);
				break;

			default:
				break;
		}
	}

	private static async Task CompareLocalFilesAsync(CancellationToken ct)
	{
		string path1 = AnsiConsole.Prompt(
			new TextPrompt<string>("Enter path to first file:"));

		string path2 = AnsiConsole.Prompt(
			new TextPrompt<string>("Enter path to second file:"));

		if (!File.Exists(path1) || !File.Exists(path2))
		{
			AnsiConsole.MarkupLine("[red]One or both files do not exist.[/]");
			return;
		}

		string content1 = await File.ReadAllTextAsync(path1, ct).ConfigureAwait(false);
		string content2 = await File.ReadAllTextAsync(path2, ct).ConfigureAwait(false);

		SideBySideDiffBuilder diffBuilder = new(new Differ());
		SideBySideDiffModel diff = diffBuilder.BuildDiffModel(content1, content2);

		Table diffTable = new()
		{
			Border = TableBorder.Rounded,
			Title = new TableTitle("[bold]File Comparison[/]"),
		};

		diffTable.AddColumn(new TableColumn(Path.GetFileName(path1).EscapeMarkup()).Width(60));
		diffTable.AddColumn(new TableColumn(Path.GetFileName(path2).EscapeMarkup()).Width(60));

		int maxLines = Math.Max(diff.OldText.Lines.Count, diff.NewText.Lines.Count);
		int displayLimit = Math.Min(maxLines, 100);

		for (int i = 0; i < displayLimit; i++)
		{
			string leftLine = i < diff.OldText.Lines.Count
				? FormatDiffLine(diff.OldText.Lines[i])
				: string.Empty;
			string rightLine = i < diff.NewText.Lines.Count
				? FormatDiffLine(diff.NewText.Lines[i])
				: string.Empty;

			diffTable.AddRow(leftLine, rightLine);
		}

		if (maxLines > 100)
		{
			diffTable.Caption = new TableTitle($"[dim]Showing first 100 of {maxLines} lines[/]");
		}

		AnsiConsole.Write(diffTable);
	}

	private static string FormatDiffLine(DiffPiece line) =>
		line.Type switch
		{
			ChangeType.Inserted => $"[green]+{line.Text?.EscapeMarkup() ?? string.Empty}[/]",
			ChangeType.Deleted => $"[red]-{line.Text?.EscapeMarkup() ?? string.Empty}[/]",
			ChangeType.Modified => $"[yellow]~{line.Text?.EscapeMarkup() ?? string.Empty}[/]",
			ChangeType.Imaginary => "[dim]~[/]",
			_ => line.Text?.EscapeMarkup() ?? string.Empty,
		};

	private static string FormatRunStatus(string status, string conclusion)
	{
		if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrEmpty(conclusion))
		{
			return conclusion.ToUpperInvariant() switch
			{
				"SUCCESS" => "[green]Success[/]",
				"FAILURE" => "[red]Failed[/]",
				"CANCELLED" => "[yellow]Cancelled[/]",
				_ => $"[dim]{conclusion.EscapeMarkup()}[/]",
			};
		}

		return status.ToUpperInvariant() switch
		{
			"IN_PROGRESS" => "[blue]Running[/]",
			"QUEUED" => "[yellow]Queued[/]",
			_ => $"[dim]{status.EscapeMarkup()}[/]",
		};
	}
}
