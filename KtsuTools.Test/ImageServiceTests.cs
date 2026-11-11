// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using KtsuTools.Commands;
using KtsuTools.Image;

using ktsu.Semantics.Paths;

using ktsu.Imaging;

using Spectre.Console;

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

	// Expected tones were measured from ImageSharp 3.1.12's BlackWhite() on the same pixels, so the
	// port keeps the tonal values the rest of the pipeline was written against.
	[TestMethod]
	[DataRow((byte)0, (byte)0, (byte)0, (byte)255, (byte)0)]
	[DataRow((byte)255, (byte)255, (byte)255, (byte)255, (byte)255)]
	[DataRow((byte)85, (byte)85, (byte)85, (byte)255, (byte)128)]
	[DataRow((byte)128, (byte)128, (byte)128, (byte)128, (byte)255)]
	[DataRow((byte)200, (byte)10, (byte)60, (byte)255, (byte)150)]
	[DataRow((byte)60, (byte)60, (byte)60, (byte)255, (byte)15)]
	[DataRow((byte)100, (byte)90, (byte)80, (byte)40, (byte)150)]
	[DataRow((byte)150, (byte)150, (byte)150, (byte)0, (byte)255)]
	public void ApplyBlackWhiteMatchesTheImageSharpFilter(byte r, byte g, byte b, byte a, byte expectedTone)
	{
		ImagePixels image = new(1, 1);
		image.SetPixel(0, 0, r, g, b, a);

		ImageService.ApplyBlackWhite(image);

		Assert.AreEqual((expectedTone, expectedTone, expectedTone, a), image.GetPixel(0, 0));
	}

	[TestMethod]
	public async Task ProcessAsyncCropsToExactlyTheOpaqueRectangle()
	{
		string root = CreateTempDirectory();
		try
		{
			string input = Path.Combine(root, "in");
			string output = Path.Combine(root, "out");
			Directory.CreateDirectory(input);
			SavePng(Path.Combine(input, "square.png"), width: 10, height: 10, new PixelBounds(3, 2, 4, 4));

			int processed = await ProcessAsync(input, output).ConfigureAwait(false);

			Assert.AreEqual(1, processed);
			ImagePixels result = ImageDecoder.Load(Path.Combine(output, "square.png"));
			Assert.AreEqual(4, result.Width);
			Assert.AreEqual(4, result.Height);
			Assert.AreEqual((byte)255, result.GetPixel(3, 3).A, "The last content column and row must survive the crop.");
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
			SavePng(Path.Combine(input, "wide.png"), width: 12, height: 12, new PixelBounds(1, 4, 6, 3));

			int processed = await ProcessAsync(input, output).ConfigureAwait(false);

			Assert.AreEqual(1, processed);
			ImagePixels result = ImageDecoder.Load(Path.Combine(output, "wide.png"));
			Assert.AreEqual(6, result.Width);
			Assert.AreEqual(6, result.Height);
			Assert.AreEqual((byte)255, result.GetPixel(5, 2).A, "The rightmost content column and bottom content row must survive the crop.");
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
			SavePng(Path.Combine(input, "b-pixel.png"), width: 10, height: 10, new PixelBounds(5, 5, 1, 1));
			SavePng(Path.Combine(input, "c-square.png"), width: 10, height: 10, new PixelBounds(2, 2, 4, 4));

			int processed = await ProcessAsync(input, output).ConfigureAwait(false);

			Assert.IsFalse(File.Exists(Path.Combine(output, "a-blank.png")), "A fully transparent image has nothing to crop to and must be skipped.");
			Assert.IsTrue(File.Exists(Path.Combine(output, "c-square.png")), "A bad image must not stop the rest of the batch.");
			Assert.AreEqual(2, processed);
			ImagePixels pixel = ImageDecoder.Load(Path.Combine(output, "b-pixel.png"));
			Assert.AreEqual(1, pixel.Width);
			Assert.AreEqual(1, pixel.Height);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task ProcessAsyncNamesTheOutputOfAJpegInputPngAndLeavesTheOriginalInPlace()
	{
		string root = CreateTempDirectory();
		try
		{
			string photo = Path.Join(root, "photo.jpg");
			await File.WriteAllBytesAsync(photo, Convert.FromBase64String(WhiteJpeg8x6)).ConfigureAwait(false);

			byte[] original = await File.ReadAllBytesAsync(photo).ConfigureAwait(false);

			// Output into the input directory: the case where the original used to be overwritten.
			int processed = await ProcessAsync(root, root).ConfigureAwait(false);

			Assert.AreEqual(1, processed);
			CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(photo).ConfigureAwait(false), "The JPEG input must not be overwritten.");
			string png = Path.Join(root, "photo.png");
			Assert.IsTrue(File.Exists(png), "A PNG output must carry the .png extension.");
			Assert.AreEqual(ImageFormat.Png, ImageDecoder.Identify(await File.ReadAllBytesAsync(png).ConfigureAwait(false)));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task ProcessAsyncSkipsAnInputWhoseOutputNameIsAlreadyTaken()
	{
		string root = CreateTempDirectory();
		try
		{
			string input = Path.Join(root, "in");
			string output = Path.Join(root, "out");
			Directory.CreateDirectory(input);
			await File.WriteAllBytesAsync(Path.Join(input, "icon.jpg"), Convert.FromBase64String(WhiteJpeg5x5)).ConfigureAwait(false);

			SavePng(Path.Join(input, "icon.png"), width: 10, height: 10, new PixelBounds(1, 1, 3, 3));

			int processed = await ProcessAsync(input, output).ConfigureAwait(false);

			Assert.AreEqual(1, processed, "Only one of two inputs that map to icon.png may be written.");
			Assert.AreEqual("icon.png", Path.GetFileName(Directory.GetFiles(output).Single()));
			ImagePixels result = ImageDecoder.Load(Path.Join(output, "icon.png"));
			Assert.AreEqual(5, result.Width, "The first input in name order (icon.jpg) keeps the name; icon.png must not overwrite it.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task ProcessAsyncRefusesNegativePaddingAndWritesNothing()
	{
		string root = CreateTempDirectory();
		try
		{
			string input = Path.Join(root, "in");
			string output = Path.Join(root, "out");
			Directory.CreateDirectory(input);
			SavePng(Path.Join(input, "a.png"), width: 40, height: 40, new PixelBounds(0, 0, 40, 40));

			int processed = await ImageService.ProcessAsync(
				AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(input),
				AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(output),
				size: 32,
				padding: -10).ConfigureAwait(false);

			Assert.AreEqual(0, processed, "Negative padding would make the content, and so the icon, larger than --size.");
			Assert.IsFalse(Directory.Exists(output), "The range checks run before the output directory is created.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-5)]
	public void SettingsRejectASizeBelowOneByNamingTheSize(int size)
	{
		ImageProcessCommand.Settings settings = new() { InputPath = "in", OutputPath = "out", Size = size, Padding = 0 };

		ValidationResult result = settings.Validate();

		Assert.IsFalse(result.Successful, "The padding message never mentioned the size that was wrong.");
		Assert.AreEqual("--size must be at least 1", result.Message);
	}

	[TestMethod]
	public void SettingsRejectNegativePadding()
	{
		ImageProcessCommand.Settings settings = new() { InputPath = "in", OutputPath = "out", Size = 32, Padding = -10 };

		ValidationResult result = settings.Validate();

		Assert.IsFalse(result.Successful);
		Assert.AreEqual("--padding must be zero or greater", result.Message);
	}

	[TestMethod]
	[DataRow(1, 0)]
	[DataRow(3, 1)]
	[DataRow(5, 2)]
	public void SettingsAcceptPaddingThatLeavesOnePixelOfContent(int size, int padding)
	{
		ImageProcessCommand.Settings settings = new() { InputPath = "in", OutputPath = "out", Size = size, Padding = padding };

		ValidationResult result = settings.Validate();

		Assert.IsTrue(result.Successful, result.Message);
	}

	[TestMethod]
	[DataRow(2, 1, 0)]
	[DataRow(3, 2, 1)]
	[DataRow(128, 64, 63)]
	[DataRow(128, int.MaxValue, 63)]
	public void SettingsRejectPaddingThatLeavesNoContent(int size, int padding, int maxPadding)
	{
		ImageProcessCommand.Settings settings = new() { InputPath = "in", OutputPath = "out", Size = size, Padding = padding };

		ValidationResult result = settings.Validate();

		Assert.IsFalse(result.Successful);
		Assert.AreEqual($"--padding must be at most {maxPadding} for --size {size}, so at least 1 pixel of content remains", result.Message);
	}

	[TestMethod]
	[DataRow(1, 0)]
	[DataRow(3, 1)]
	public async Task ProcessAsyncAcceptsTheSmallestValidSizes(int size, int padding)
	{
		string root = CreateTempDirectory();
		try
		{
			string input = Path.Join(root, "in");
			string output = Path.Join(root, "out");
			Directory.CreateDirectory(input);
			SavePng(Path.Join(input, "a.png"), width: 40, height: 40, new PixelBounds(0, 0, 40, 40));

			int processed = await ImageService.ProcessAsync(
				AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(input),
				AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(output),
				size: size,
				padding: padding).ConfigureAwait(false);

			Assert.AreEqual(1, processed, "A size the validation accepts must not be rejected with a padding message.");
			ImagePixels result = ImageDecoder.Load(Path.Join(output, "a.png"));
			Assert.AreEqual(size, result.Width);
			Assert.AreEqual(size, result.Height);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task ProcessAsyncRefusesPaddingThatLeavesNoContentAndWritesNothing()
	{
		string root = CreateTempDirectory();
		try
		{
			string input = Path.Join(root, "in");
			string output = Path.Join(root, "out");
			Directory.CreateDirectory(input);
			SavePng(Path.Join(input, "a.png"), width: 40, height: 40, new PixelBounds(0, 0, 40, 40));

			int processed = await ImageService.ProcessAsync(
				AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(input),
				AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(output),
				size: 2,
				padding: 1).ConfigureAwait(false);

			Assert.AreEqual(0, processed);
			Assert.IsFalse(Directory.Exists(output), "The range checks run before the output directory is created.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public void SettingsAcceptTheDefaults()
	{
		ImageProcessCommand.Settings settings = new() { InputPath = "in", OutputPath = "out", Size = 128, Padding = 0 };

		Assert.IsTrue(settings.Validate().Successful);
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

	private static void SavePng(string path, int width, int height, PixelBounds? content)
	{
		ImagePixels image = new(width, height);
		if (content is PixelBounds rectangle)
		{
			for (int y = rectangle.Y; y < rectangle.Y + rectangle.Height; y++)
			{
				for (int x = rectangle.X; x < rectangle.X + rectangle.Width; x++)
				{
					image.SetPixel(x, y, 255, 255, 255, 255);
				}
			}
		}

		PngEncoder.Save(image, path);
	}

	// Plain white baseline JPEGs, 8x6 and 5x5. ktsu.Imaging decodes JPEG but does not encode it, so
	// the fixtures are stored rather than written by the test.
	private const string WhiteJpeg8x6 = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAGAAgDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD9U6KKKAP/2Q==";

	private const string WhiteJpeg5x5 = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAFAAUDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD9U6KKKAP/2Q==";
}
