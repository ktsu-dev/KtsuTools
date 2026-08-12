// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using KtsuTools.CodeGen;

[TestClass]
public class CodeGenServiceTests
{
	private static FunctionDeclaration BuildSampleFunction() => new()
	{
		Name = "Add",
		ReturnType = "int",
		Parameters =
		[
			new ParameterNode { Name = "a", Type = "int" },
			new ParameterNode { Name = "b", Type = "int" },
		],
		Body =
		[
			new ReturnStatement { Expression = "a + b" },
		],
	};

	[TestMethod]
	public void CSharpGeneratorGeneratesSignatureBodyAndReturn()
	{
		CSharpGenerator generator = new();
		string code = generator.Generate(BuildSampleFunction());
		StringAssert.Contains(code, "public int Add(int a, int b)");
		StringAssert.Contains(code, "return a + b;");
		StringAssert.Contains(code, "{");
		StringAssert.Contains(code, "}");
	}

	[TestMethod]
	public void CSharpGeneratorMapsPythonStrToString()
	{
		CSharpGenerator generator = new();
		FunctionDeclaration fn = new()
		{
			Name = "Greet",
			ReturnType = "str",
			Parameters = [new ParameterNode { Name = "name", Type = "str" }],
		};
		string code = generator.Generate(fn);
		StringAssert.Contains(code, "public string Greet(string name)");
	}

	[TestMethod]
	public void PythonGeneratorGeneratesDefSignatureAndReturn()
	{
		PythonGenerator generator = new();
		string code = generator.Generate(BuildSampleFunction());
		StringAssert.Contains(code, "def Add");
		StringAssert.Contains(code, "return a + b");
	}

	[TestMethod]
	public void GeneratorsReportConsistentLanguageMetadata()
	{
		CSharpGenerator csharp = new();
		PythonGenerator python = new();
		Assert.AreEqual("csharp", csharp.LanguageId);
		Assert.AreEqual("cs", csharp.FileExtension);
		Assert.AreEqual("python", python.LanguageId);
		Assert.AreEqual("py", python.FileExtension);
	}
}
