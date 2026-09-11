// Copyright (c) 2023-2026 ktsu-dev contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ktsu.KtsuTools.Test")]

namespace KtsuTools.CodeGen;

using System.Collections.ObjectModel;

using ktsu.Coder.Ast;
using ktsu.Coder.Languages;
using ktsu.Coder.Serialization;
using ktsu.Semantics.Paths;

using Spectre.Console;

/// <summary>
/// Generates source from a YAML description of an abstract syntax tree.
/// </summary>
/// <remarks>
/// The AST, the YAML on both sides of it and every generator come from
/// <see href="https://github.com/ktsu-dev/Coder">ktsu.Coder</see>. This module used to carry its own
/// — an <c>IAstNode</c>, five node types, a hand-written YAML reader and a C# and a Python emitter,
/// all of which that package already had along with C++, JavaScript, a round trip back to YAML, and
/// a test suite. What is left here is what a command-line front end is actually for: finding the
/// file, choosing the generator, and putting the result somewhere.
/// </remarks>
public class CodeGenService
{
	/// <summary>
	/// The generators, by the name a caller asks for them under.
	/// </summary>
	private static readonly ReadOnlyDictionary<string, ILanguageGenerator> Generators =
		new(new Dictionary<string, ILanguageGenerator>(StringComparer.OrdinalIgnoreCase)
		{
			["csharp"] = new CSharpGenerator(),
			["cpp"] = new CppGenerator(),
			["javascript"] = new JavaScriptGenerator(),
			["python"] = new PythonGenerator(),
		});

	/// <summary>
	/// Gets the languages this command can generate.
	/// </summary>
	public static IEnumerable<string> Languages => Generators.Keys;

	/// <summary>
	/// Generates code from a YAML AST definition file.
	/// </summary>
	/// <param name="inputFile">Absolute path to the YAML AST input file.</param>
	/// <param name="language">Target language identifier (e.g. "csharp", "cpp").</param>
	/// <param name="outputFile">Optional absolute path to write generated code to. If null, writes to console.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>Exit code (0 for success).</returns>
	/// <remarks>
	/// CA1822 and S2325 are the same complaint from two analyzers, and the answer to both is that
	/// this service is a singleton injected into <c>CodeGenCommand</c>'s constructor: a static
	/// method here would leave the command holding a dependency it takes and never uses.
	/// </remarks>
#pragma warning disable CA1822, S2325 // Mark members as static
	public async Task<int> GenerateAsync(AbsoluteFilePath inputFile, string language, AbsoluteFilePath? outputFile = null, CancellationToken ct = default)
#pragma warning restore CA1822, S2325
	{
		Ensure.NotNull(inputFile);
		Ensure.NotNull(language);

		string fullPath = inputFile.ToString();

		if (!File.Exists(fullPath))
		{
			AnsiConsole.MarkupLine($"[red]Error: Input file '{fullPath.EscapeMarkup()}' does not exist.[/]");
			return 1;
		}

		if (!Generators.TryGetValue(language, out ILanguageGenerator? generator))
		{
			AnsiConsole.MarkupLine($"[red]Error: Unknown language '{language.EscapeMarkup()}'.[/]");
			AnsiConsole.MarkupLine($"[blue]Supported languages: {string.Join(", ", Generators.Keys)}[/]");
			return 1;
		}

		AnsiConsole.MarkupLine($"[bold]Code Generation[/] - {generator.DisplayName}");

		string yaml = await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);

		if (!TryRead(yaml, out AstNode? node))
		{
			return 1;
		}

		// A generator says whether it can write a node rather than throwing partway through one, so
		// a file naming something the target has no form for is reported here instead of arriving
		// as half a file.
		if (!generator.CanGenerate(node!))
		{
			AnsiConsole.MarkupLine(
				$"[red]Error: {generator.DisplayName.EscapeMarkup()} cannot generate a {node!.GetNodeTypeName().EscapeMarkup()}.[/]");
			return 1;
		}

		string generated = generator.Generate(node!);

		if (outputFile is not null)
		{
			string outputPath = outputFile.ToString();
			await File.WriteAllTextAsync(outputPath, generated, ct).ConfigureAwait(false);
			AnsiConsole.MarkupLine($"[green]Generated code written to: {outputPath.EscapeMarkup()}[/]");
			return 0;
		}

		AnsiConsole.Write(new Panel(generated.EscapeMarkup())
			.Header($"[blue]{generator.DisplayName} Output[/]")
			.Border(BoxBorder.Rounded));

		return 0;
	}

	/// <summary>
	/// Reads the AST a document describes, reporting rather than throwing when it describes none.
	/// </summary>
	/// <param name="yaml">The document.</param>
	/// <param name="node">The AST it described.</param>
	/// <returns><see langword="true"/> when one was read.</returns>
	internal static bool TryRead(string yaml, out AstNode? node)
	{
		node = null;

		try
		{
			node = new YamlDeserializer().Deserialize(yaml);
		}
		catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidCastException or ArgumentException)
		{
			AnsiConsole.MarkupLine($"[red]Error: could not read the document: {ex.Message.EscapeMarkup()}[/]");
			return false;
		}

		if (node is null)
		{
			AnsiConsole.MarkupLine("[red]Error: the document does not describe an AST node.[/]");
			return false;
		}

		return true;
	}
}
