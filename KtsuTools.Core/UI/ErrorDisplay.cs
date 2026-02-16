// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Core.UI;

using System;
using System.Collections.Generic;
using Spectre.Console;

public static class ErrorDisplay
{
	public static void ShowError(string message) =>
		AnsiConsole.MarkupLine($"[red bold]Error:[/] {message.EscapeMarkup()}");

	public static void ShowWarning(string message) =>
		AnsiConsole.MarkupLine($"[yellow bold]Warning:[/] {message.EscapeMarkup()}");

	public static void ShowSuccess(string message) =>
		AnsiConsole.MarkupLine($"[green bold]Success:[/] {message.EscapeMarkup()}");

	public static void ShowException(Exception ex) =>
		AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes);

	public static void ShowErrors(string title, IEnumerable<string> errors)
	{
		Ensure.NotNull(errors);

		Table table = new Table()
			.Border(TableBorder.Rounded)
			.BorderColor(Color.Red)
			.AddColumn(new TableColumn($"[red bold]{title.EscapeMarkup()}[/]"));

		foreach (string error in errors)
		{
			table.AddRow(error.EscapeMarkup());
		}

		AnsiConsole.Write(table);
	}
}
