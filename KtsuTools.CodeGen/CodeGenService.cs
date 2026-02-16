// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.CodeGen;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using KtsuTools.Core.Services.Settings;
using Spectre.Console;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

#pragma warning disable CA1002 // Do not expose generic lists - needed for AST node model

/// <summary>
/// Base class for all AST nodes.
/// </summary>
public abstract class AstNode
{
	/// <summary>Gets or sets the node type name.</summary>
	public abstract string NodeType { get; }
}

/// <summary>
/// A function declaration AST node.
/// </summary>
public class FunctionDeclaration : AstNode
{
	/// <inheritdoc/>
	public override string NodeType => "functionDeclaration";

	/// <summary>Gets or sets the function name.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the return type.</summary>
	public string ReturnType { get; set; } = "void";

	/// <summary>Gets or sets the parameters.</summary>
	public Collection<ParameterNode> Parameters { get; init; } = [];

	/// <summary>Gets or sets the body statements.</summary>
	public Collection<AstNode> Body { get; init; } = [];
}

/// <summary>
/// A parameter AST node.
/// </summary>
public class ParameterNode : AstNode
{
	/// <inheritdoc/>
	public override string NodeType => "parameter";

	/// <summary>Gets or sets the parameter name.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the type.</summary>
	public string Type { get; set; } = string.Empty;

	/// <summary>Gets or sets a value indicating whether this parameter is optional.</summary>
	public bool IsOptional { get; set; }

	/// <summary>Gets or sets the default value.</summary>
	public string? DefaultValue { get; set; }
}

/// <summary>
/// A variable declaration AST node.
/// </summary>
public class VariableDeclaration : AstNode
{
	/// <inheritdoc/>
	public override string NodeType => "variableDeclaration";

	/// <summary>Gets or sets the variable name.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the type.</summary>
	public string? Type { get; set; }

	/// <summary>Gets or sets the initial value expression.</summary>
	public string? InitialValue { get; set; }

	/// <summary>Gets or sets a value indicating whether this is a constant.</summary>
	public bool IsConstant { get; set; }
}

/// <summary>
/// A return statement AST node.
/// </summary>
public class ReturnStatement : AstNode
{
	/// <inheritdoc/>
	public override string NodeType => "returnStatement";

	/// <summary>Gets or sets the return expression.</summary>
	public string? Expression { get; set; }
}

/// <summary>
/// Interface for language code generators.
/// </summary>
public interface ILanguageGenerator
{
	/// <summary>Gets the language identifier.</summary>
	public string LanguageId { get; }

	/// <summary>Gets the display name.</summary>
	public string DisplayName { get; }

	/// <summary>Gets the file extension.</summary>
	public string FileExtension { get; }

	/// <summary>Generates code from an AST.</summary>
	public string Generate(FunctionDeclaration declaration);
}

/// <summary>
/// C# code generator.
/// </summary>
public class CSharpGenerator : ILanguageGenerator
{
	/// <inheritdoc/>
	public string LanguageId => "csharp";

	/// <inheritdoc/>
	public string DisplayName => "C#";

	/// <inheritdoc/>
	public string FileExtension => "cs";

	/// <inheritdoc/>
	public string Generate(FunctionDeclaration declaration)
	{
		Ensure.NotNull(declaration);

		StringBuilder sb = new();
		string returnType = MapType(declaration.ReturnType);
		string parameters = string.Join(", ", declaration.Parameters.Select(p =>
		{
			string paramType = MapType(p.Type);
			string defaultVal = p.IsOptional && p.DefaultValue is not null
				? $" = {p.DefaultValue}"
				: string.Empty;
			return $"{paramType} {p.Name}{defaultVal}";
		}));

		sb.AppendLine(CultureInfo.InvariantCulture, $"public {returnType} {declaration.Name}({parameters})");
		sb.AppendLine("{");

		foreach (AstNode statement in declaration.Body)
		{
			if (statement is ReturnStatement ret)
			{
				sb.AppendLine(CultureInfo.InvariantCulture, $"    return {ret.Expression};");
			}
			else if (statement is VariableDeclaration varDecl)
			{
				string varType = varDecl.Type is not null ? MapType(varDecl.Type) : "var";
				string init = varDecl.InitialValue is not null ? $" = {varDecl.InitialValue}" : string.Empty;
				string keyword = varDecl.IsConstant ? "const " : string.Empty;
				sb.AppendLine(CultureInfo.InvariantCulture, $"    {keyword}{varType} {varDecl.Name}{init};");
			}
		}

		sb.AppendLine("}");
		return sb.ToString();
	}

	private static string MapType(string type) => type switch
	{
		"str" or "string" => "string",
		"int" => "int",
		"float" => "float",
		"double" => "double",
		"bool" => "bool",
		"void" => "void",
		_ => type,
	};
}

/// <summary>
/// Python code generator.
/// </summary>
public class PythonGenerator : ILanguageGenerator
{
	/// <inheritdoc/>
	public string LanguageId => "python";

	/// <inheritdoc/>
	public string DisplayName => "Python";

	/// <inheritdoc/>
	public string FileExtension => "py";

	/// <inheritdoc/>
	public string Generate(FunctionDeclaration declaration)
	{
		Ensure.NotNull(declaration);

		StringBuilder sb = new();
		string parameters = string.Join(", ", declaration.Parameters.Select(p =>
		{
			string typeHint = MapType(p.Type);
			string defaultVal = p.IsOptional && p.DefaultValue is not null
				? $" = {p.DefaultValue}"
				: string.Empty;
			return $"{p.Name}: {typeHint}{defaultVal}";
		}));

		string returnHint = declaration.ReturnType != "void"
			? $" -> {MapType(declaration.ReturnType)}"
			: string.Empty;

		sb.AppendLine(CultureInfo.InvariantCulture, $"def {declaration.Name}({parameters}){returnHint}:");

		if (declaration.Body.Count == 0)
		{
			sb.AppendLine("    pass");
		}
		else
		{
			foreach (AstNode statement in declaration.Body)
			{
				if (statement is ReturnStatement ret)
				{
					sb.AppendLine(CultureInfo.InvariantCulture, $"    return {ret.Expression}");
				}
				else if (statement is VariableDeclaration varDecl)
				{
					string init = varDecl.InitialValue ?? "None";
					sb.AppendLine(CultureInfo.InvariantCulture, $"    {varDecl.Name} = {init}");
				}
			}
		}

		return sb.ToString();
	}

	private static string MapType(string type) => type switch
	{
		"int" => "int",
		"string" or "str" => "str",
		"bool" => "bool",
		"float" or "double" => "float",
		"void" => "None",
		_ => type,
	};
}

/// <summary>
/// Service for generating code from YAML AST definitions.
/// </summary>
public class CodeGenService(ISettingsService settingsService)
{
	private readonly ISettingsService settingsService = settingsService;

	private static readonly Dictionary<string, ILanguageGenerator> Generators = new(StringComparer.OrdinalIgnoreCase)
	{
		["csharp"] = new CSharpGenerator(),
		["python"] = new PythonGenerator(),
	};

	/// <summary>
	/// Generates code from a YAML AST definition file.
	/// </summary>
	public async Task<int> GenerateAsync(string inputFile, string language, string? outputFile = null, CancellationToken ct = default)
	{
		_ = settingsService;
		Ensure.NotNull(inputFile);
		Ensure.NotNull(language);

		string fullPath = Path.GetFullPath(inputFile);

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

		// Read and parse YAML
		string yamlContent = await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);

		FunctionDeclaration? function = ParseYaml(yamlContent);

		if (function is null)
		{
			AnsiConsole.MarkupLine("[red]Error: Could not parse YAML as a function declaration.[/]");
			return 1;
		}

		// Generate code
		string generatedCode = generator.Generate(function);

		// Output
		if (outputFile is not null)
		{
			string outputPath = Path.GetFullPath(outputFile);
			await File.WriteAllTextAsync(outputPath, generatedCode, ct).ConfigureAwait(false);
			AnsiConsole.MarkupLine($"[green]Generated code written to: {outputPath.EscapeMarkup()}[/]");
		}
		else
		{
			AnsiConsole.Write(new Panel(generatedCode.EscapeMarkup())
				.Header($"[blue]{generator.DisplayName} Output[/]")
				.Border(BoxBorder.Rounded));
		}

		return 0;
	}

	private static FunctionDeclaration? ParseYaml(string yamlContent)
	{
		try
		{
			IDeserializer deserializer = new DeserializerBuilder()
				.WithNamingConvention(CamelCaseNamingConvention.Instance)
				.IgnoreUnmatchedProperties()
				.Build();

			Dictionary<string, object> root = deserializer.Deserialize<Dictionary<string, object>>(yamlContent);

			if (root.TryGetValue("functionDeclaration", out object? funcObj) && funcObj is Dictionary<object, object> funcDict)
			{
				return ParseFunctionDeclaration(funcDict);
			}

			return null;
		}
		catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidCastException or KeyNotFoundException)
		{
			AnsiConsole.MarkupLine($"[yellow]YAML parsing warning: {ex.Message.EscapeMarkup()}[/]");
			return null;
		}
	}

	private static FunctionDeclaration ParseFunctionDeclaration(Dictionary<object, object> dict)
	{
		FunctionDeclaration func = new()
		{
			Name = GetStringValue(dict, "name") ?? "unnamed",
			ReturnType = GetStringValue(dict, "returnType") ?? "void",
		};

		if (dict.TryGetValue("parameters", out object? paramsObj) && paramsObj is List<object> paramsList)
		{
			foreach (object paramObj in paramsList)
			{
				if (paramObj is Dictionary<object, object> paramDict)
				{
					func.Parameters.Add(new ParameterNode
					{
						Name = GetStringValue(paramDict, "name") ?? "arg",
						Type = GetStringValue(paramDict, "type") ?? "string",
						IsOptional = GetBoolValue(paramDict, "isOptional"),
						DefaultValue = GetStringValue(paramDict, "defaultValue"),
					});
				}
			}
		}

		if (dict.TryGetValue("body", out object? bodyObj) && bodyObj is List<object> bodyList)
		{
			foreach (object stmtObj in bodyList)
			{
				if (stmtObj is Dictionary<object, object> stmtDict)
				{
					AstNode? node = ParseStatement(stmtDict);
					if (node is not null)
					{
						func.Body.Add(node);
					}
				}
			}
		}

		return func;
	}

	private static AstNode? ParseStatement(Dictionary<object, object> dict)
	{
		if (dict.ContainsKey("returnStatement") && dict["returnStatement"] is Dictionary<object, object> retDict)
		{
			return new ReturnStatement
			{
				Expression = GetStringValue(retDict, "expression"),
			};
		}

		if (dict.ContainsKey("variableDeclaration") && dict["variableDeclaration"] is Dictionary<object, object> varDict)
		{
			return new VariableDeclaration
			{
				Name = GetStringValue(varDict, "name") ?? "x",
				Type = GetStringValue(varDict, "type"),
				InitialValue = GetStringValue(varDict, "initialValue"),
				IsConstant = GetBoolValue(varDict, "isConstant"),
			};
		}

		return null;
	}

	private static string? GetStringValue(Dictionary<object, object> dict, string key) =>
		dict.TryGetValue(key, out object? value) ? value?.ToString() : null;

	private static bool GetBoolValue(Dictionary<object, object> dict, string key) =>
		dict.TryGetValue(key, out object? value) && value is bool b && b;
}

#pragma warning restore CA1002
