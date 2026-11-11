// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Text;
using ktsu.Semantics.Paths;
using KtsuTools.Core.Services.Process;
using KtsuTools.Packages;
using Moq;

/// <summary>
/// <c>packages update</c> and the <c>migrate-cpm</c> props merge rewrite files in place, so a file
/// must come back byte for byte as it was apart from the version that changed, byte order mark
/// included.
/// </summary>
[TestClass]
public class PackagesEncodingTests
{
	private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

	private string root = string.Empty;

	[TestInitialize]
	public void CreateWorkspace()
	{
		root = Path.Combine(Path.GetTempPath(), "ktsu-packages-encoding-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
	}

	[TestCleanup]
	public void RemoveWorkspace()
	{
		if (Directory.Exists(root))
		{
			Directory.Delete(root, recursive: true);
		}
	}

	private static byte[] Encode(string text, bool bom) =>
		[.. bom ? Utf8Bom : [], .. new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text)];

	private static string Project(string version) =>
		"<Project Sdk=\"Microsoft.NET.Sdk\">\r\n" +
		"  <ItemGroup>\r\n" +
		$"    <PackageReference Include=\"Polly\" Version=\"{version}\" />\r\n" +
		"  </ItemGroup>\r\n" +
		"</Project>\r\n";

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public async Task UpdateRewritesOnlyTheVersionAndKeepsTheByteOrderMarkAsItWas(bool bom)
	{
		string projectPath = Path.Combine(root, "A.csproj");
		await File.WriteAllBytesAsync(projectPath, Encode(Project("8.0.0"), bom)).ConfigureAwait(false);

		await PackagesService.UpdatePackageVersionInFileAsync(projectPath, "Polly", "8.1.0", CancellationToken.None).ConfigureAwait(false);

		CollectionAssert.AreEqual(Encode(Project("8.1.0"), bom), await File.ReadAllBytesAsync(projectPath).ConfigureAwait(false));
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public async Task MigrateCpmPropsMergeRewritesOnlyTheVersionAndKeepsTheByteOrderMarkAsItWas(bool bom)
	{
		static string Props(string version) =>
			"<Project>\r\n" +
			"  <ItemGroup>\r\n" +
			$"    <PackageVersion Include=\"Polly\" Version=\"{version}\" />\r\n" +
			"  </ItemGroup>\r\n" +
			"</Project>\r\n";

		string propsPath = Path.Combine(root, "Directory.Packages.props");
		await File.WriteAllBytesAsync(propsPath, Encode(Props("8.0.0"), bom)).ConfigureAwait(false);
		string projectDirectory = Path.Combine(root, "A");
		Directory.CreateDirectory(projectDirectory);
		await File.WriteAllTextAsync(Path.Combine(projectDirectory, "A.csproj"), Project("8.1.0")).ConfigureAwait(false);

		int exitCode = await new PackagesService(Mock.Of<IProcessService>())
			.MigrateToCpmAsync(AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root))
			.ConfigureAwait(false);

		Assert.AreEqual(0, exitCode);
		CollectionAssert.AreEqual(Encode(Props("8.1.0"), bom), await File.ReadAllBytesAsync(propsPath).ConfigureAwait(false));
	}
}
