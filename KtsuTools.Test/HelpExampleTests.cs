// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;
using Spectre.Console.Rendering;

[TestClass]
public class HelpExampleTests
{
	/// <summary>
	/// Every example printed by <c>ktools &lt;cmd&gt; --help</c> must get past the parser, so an option
	/// that becomes required, or is renamed, cannot leave an example that fails the moment it is run.
	/// Spectre's own <c>ValidateExamples</c> only checks that an example tokenizes, not that its
	/// required options are present, so each example is run up to the point the command would start.
	/// </summary>
	[TestMethod]
	public void EveryHelpExampleParses()
	{
		List<string[]> examples = CollectExamples();
		Assert.IsNotEmpty(examples, "The help model should list the registered examples.");

		List<string> failures = [];
		foreach (string[] example in examples)
		{
			CommandApp app = new();
			app.Configure(config =>
			{
				Program.ConfigureCommands(config);
				config.PropagateExceptions();
				config.SetInterceptor(new StopBeforeExecuting());
			});

			try
			{
				app.Run(example);
				failures.Add($"'{string.Join(' ', example)}' ran without reaching the interceptor");
			}
			catch (ExampleParsedException)
			{
				// Parsed, bound and passed the required-option checks.
			}
			catch (CommandAppException exception)
			{
				failures.Add($"'{string.Join(' ', example)}': {exception.Message}");
			}
		}

		Assert.IsEmpty(failures, "Help examples that fail as printed: " + string.Join("; ", failures));
	}

	private static List<string[]> CollectExamples()
	{
		RecordingHelpProvider recorder = new();
		CommandApp app = new();
		app.Configure(config =>
		{
			Program.ConfigureCommands(config);
			config.SetHelpProvider(recorder);
		});

		app.Run(["--help"]);

		Assert.IsNotNull(recorder.Model, "Running --help should hand the help provider the command model.");
		List<string[]> examples = [.. recorder.Model.Examples];
		AddExamples(recorder.Model.Commands, examples);
		return examples;
	}

	private static void AddExamples(IEnumerable<ICommandInfo> commands, List<string[]> examples)
	{
		foreach (ICommandInfo command in commands)
		{
			examples.AddRange(command.Examples);
			AddExamples(command.Commands, examples);
		}
	}

	private sealed class RecordingHelpProvider : IHelpProvider
	{
		public ICommandModel? Model { get; private set; }

		public IEnumerable<IRenderable> Write(ICommandModel model, ICommandInfo? command)
		{
			Model = model;
			return [];
		}
	}

	private sealed class StopBeforeExecuting : ICommandInterceptor
	{
		public void Intercept(CommandContext context, CommandSettings settings) => throw new ExampleParsedException();
	}

	private sealed class ExampleParsedException : Exception
	{
		public ExampleParsedException()
		{
		}

		public ExampleParsedException(string message)
			: base(message)
		{
		}

		public ExampleParsedException(string message, Exception innerException)
			: base(message, innerException)
		{
		}
	}
}
