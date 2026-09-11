// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using ktsu.Coder.Ast;
using ktsu.Semantics.Paths;
using KtsuTools.CodeGen;

/// <summary>
/// What the <c>codegen</c> command does with a document, now that the AST, the reader and every
/// generator belong to <c>ktsu.Coder</c>.
/// </summary>
/// <remarks>
/// These no longer test a generator — that package tests its own, across four languages and far
/// more thoroughly than a copy here ever did. What is left to test is the part this module still
/// owns: that a document reaches a generator, that the right one is chosen, and that a document
/// which describes nothing is reported rather than thrown.
/// </remarks>
[TestClass]
public class CodeGenServiceTests
{
	/// <summary>A function, in the shape Coder's reader expects.</summary>
	private const string SampleYaml =
		"""
		functionDeclaration:
		  name: Add
		  returnType: int
		  parameters:
		    - name: a
		      type: int
		    - name: b
		      type: int
		""";

	/// <summary>
	/// The document reaches an AST rather than a bespoke parser's idea of one.
	/// </summary>
	[TestMethod]
	public void ADocumentIsReadAsAnAst()
	{
		Assert.IsTrue(CodeGenService.TryRead(SampleYaml, out AstNode? node));

		FunctionDeclaration function = (FunctionDeclaration)node!;

		Assert.AreEqual("Add", function.Name);
		Assert.HasCount(2, function.Parameters);
	}

	/// <summary>
	/// A document that is well-formed YAML and describes no node is an error to report, not an
	/// exception to let out of a command.
	/// </summary>
	[TestMethod]
	public void ADocumentDescribingNoNodeIsReported() =>
		Assert.IsFalse(CodeGenService.TryRead("somethingElse: 3", out _));

	/// <summary>
	/// A document that is not YAML at all is the same kind of answer.
	/// </summary>
	[TestMethod]
	public void ADocumentThatIsNotYamlIsReported() =>
		Assert.IsFalse(CodeGenService.TryRead("\t- : :\n  bad", out _));

	/// <summary>
	/// The four languages the command offers are the four <c>ktsu.Coder</c> has. Two of them —
	/// C++ and JavaScript — had no generator here at all before.
	/// </summary>
	[TestMethod]
	public void EveryLanguageCoderHasIsOffered() =>
		Assert.AreEqual(
			"cpp, csharp, javascript, python",
			string.Join(", ", CodeGenService.Languages.Order(StringComparer.Ordinal)));

	/// <summary>
	/// End to end: a file in, a file out, in the language asked for.
	/// </summary>
	[TestMethod]
	public async Task GeneratingWritesTheRequestedLanguageToTheOutputFile()
	{
		string directory = Path.Combine(Path.GetTempPath(), $"codegen-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);

		try
		{
			string input = Path.Combine(directory, "add.yaml");
			string output = Path.Combine(directory, "add.hpp");
			await File.WriteAllTextAsync(input, SampleYaml, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

			int exit = await new CodeGenService().GenerateAsync(
				AbsoluteFilePath.Create<AbsoluteFilePath>(input),
				"cpp",
				AbsoluteFilePath.Create<AbsoluteFilePath>(output),
				TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

			Assert.AreEqual(0, exit);
			Assert.Contains("int Add(int a, int b)", await File.ReadAllTextAsync(output, TestContext.CancellationTokenSource.Token).ConfigureAwait(false));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	/// <summary>
	/// A language nobody has a generator for is an exit code and a list, not a crash.
	/// </summary>
	[TestMethod]
	public async Task AnUnknownLanguageIsRefused()
	{
		string directory = Path.Combine(Path.GetTempPath(), $"codegen-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);

		try
		{
			string input = Path.Combine(directory, "add.yaml");
			await File.WriteAllTextAsync(input, SampleYaml, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

			int exit = await new CodeGenService().GenerateAsync(
				AbsoluteFilePath.Create<AbsoluteFilePath>(input),
				"cobol",
				ct: TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

			Assert.AreEqual(1, exit);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	/// <summary>Gets or sets the context the test platform supplies.</summary>
	public TestContext TestContext { get; set; } = null!;
}
