// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using KtsuTools.Image;

using ktsu.Semantics.Paths;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

[TestClass]
public class ImageServiceTests
{
	[TestMethod]
	public void ParseHexColorAcceptsSixDigitForm()
	{
		Rgba32 color = ImageService.ParseHexColor("#FF8800");
		Assert.AreEqual((byte)0xFF, color.R);
		Assert.AreEqual((byte)0x88, color.G);
		Assert.AreEqual((byte)0x00, color.B);
		Assert.AreEqual((byte)0xFF, color.A);
	}

	[TestMethod]
	public void ParseHexColorAcceptsThreeDigitShorthand()
	{
		Rgba32 color = ImageService.ParseHexColor("#F80");
		Assert.AreEqual((byte)0xFF, color.R);
		Assert.AreEqual((byte)0x88, color.G);
		Assert.AreEqual((byte)0x00, color.B);
	}

	[TestMethod]
	public void ParseHexColorAcceptsHexWithoutHashPrefix()
	{
		Rgba32 color = ImageService.ParseHexColor("ABCDEF");
		Assert.AreEqual((byte)0xAB, color.R);
		Assert.AreEqual((byte)0xCD, color.G);
		Assert.AreEqual((byte)0xEF, color.B);
	}

	[TestMethod]
	public void ParseHexColorRejectsInvalidLength() =>
		Assert.ThrowsExactly<ArgumentException>(() => ImageService.ParseHexColor("#1234"));

	[TestMethod]
	public async Task ProcessAsyncCropsToExactlyTheOpaqueRectangle()
	{
		string root = CreateTempDirectory();
		try
		{
			string input = Path.Combine(root, "in");
			string output = Path.Combine(root, "out");
			Directory.CreateDirectory(input);
			SavePng(Path.Combine(input, "square.png"), width: 10, height: 10, new Rectangle(3, 2, 4, 4));

			int processed = await ProcessAsync(input, output).ConfigureAwait(false);

			Assert.AreEqual(1, processed);
			using Image<Rgba32> result = await Image.LoadAsync<Rgba32>(Path.Combine(output, "square.png")).ConfigureAwait(false);
			Assert.AreEqual(4, result.Width);
			Assert.AreEqual(4, result.Height);
			Assert.AreEqual((byte)255, result[3, 3].A, "The last content column and row must survive the crop.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task ProcessAsyncCropsNonSquareContentWithoutLosingAnEdge()
	{
		string root = CreateTempDirectory();
		try
		{
			string input = Path.Combine(root, "in");
			string output = Path.Combine(root, "out");
			Directory.CreateDirectory(input);
			SavePng(Path.Combine(input, "wide.png"), width: 12, height: 12, new Rectangle(1, 4, 6, 3));

			int processed = await ProcessAsync(input, output).ConfigureAwait(false);

			Assert.AreEqual(1, processed);
			using Image<Rgba32> result = await Image.LoadAsync<Rgba32>(Path.Combine(output, "wide.png")).ConfigureAwait(false);
			Assert.AreEqual(6, result.Width);
			Assert.AreEqual(6, result.Height);
			Assert.AreEqual((byte)255, result[5, 2].A, "The rightmost content column and bottom content row must survive the crop.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task ProcessAsyncSkipsBlankAndSinglePixelImagesAndFinishesTheBatch()
	{
		string root = CreateTempDirectory();
		try
		{
			string input = Path.Combine(root, "in");
			string output = Path.Combine(root, "out");
			Directory.CreateDirectory(input);
			SavePng(Path.Combine(input, "a-blank.png"), width: 10, height: 10, content: null);
			SavePng(Path.Combine(input, "b-pixel.png"), width: 10, height: 10, new Rectangle(5, 5, 1, 1));
			SavePng(Path.Combine(input, "c-square.png"), width: 10, height: 10, new Rectangle(2, 2, 4, 4));

			int processed = await ProcessAsync(input, output).ConfigureAwait(false);

			Assert.IsFalse(File.Exists(Path.Combine(output, "a-blank.png")), "A fully transparent image has nothing to crop to and must be skipped.");
			Assert.IsTrue(File.Exists(Path.Combine(output, "c-square.png")), "A bad image must not stop the rest of the batch.");
			Assert.AreEqual(2, processed);
			using Image<Rgba32> pixel = await Image.LoadAsync<Rgba32>(Path.Combine(output, "b-pixel.png")).ConfigureAwait(false);
			Assert.AreEqual(1, pixel.Width);
			Assert.AreEqual(1, pixel.Height);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	private static Task<int> ProcessAsync(string input, string output) =>
		ImageService.ProcessAsync(
			AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(input),
			AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(output),
			size: 128,
			padding: 0);

	private static string CreateTempDirectory()
	{
		string root = Path.Combine(Path.GetTempPath(), "ktsutools-image-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		return root;
	}

	private static void SavePng(string path, int width, int height, Rectangle? content)
	{
		using Image<Rgba32> image = new(width, height, new Rgba32(0, 0, 0, 0));
		if (content is Rectangle rectangle)
		{
			for (int y = rectangle.Top; y < rectangle.Bottom; y++)
			{
				for (int x = rectangle.Left; x < rectangle.Right; x++)
				{
					image[x, y] = new Rgba32(255, 255, 255, 255);
				}
			}
		}

		image.SaveAsPng(path);
	}
}
