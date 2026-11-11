// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Merge;

using System.Text;

/// <summary>
/// A file's text together with the encoding and byte order mark it was stored with, so the text can
/// be written back the same way it was read.
/// </summary>
/// <param name="Text">The decoded text, without the byte order mark.</param>
/// <param name="Encoding">The encoding the text was decoded with. Throws rather than substituting.</param>
/// <param name="Preamble">The byte order mark the file started with, or empty when it had none.</param>
internal sealed record EncodedText(string Text, Encoding Encoding, byte[] Preamble)
{
	private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
	private static readonly Encoding StrictUtf16Le = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
	private static readonly Encoding StrictUtf16Be = new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);
	private static readonly Encoding StrictUtf32Le = new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true);
	private static readonly Encoding StrictUtf32Be = new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true);

	/// <summary>
	/// Latin-1 maps every byte to the code point of the same value, so a file that is not valid
	/// UTF-8 decodes without loss and encodes back to exactly the same bytes. Characters above
	/// U+00FF can't be written in it, and the exception fallback refuses them instead of writing '?'.
	/// </summary>
	private static readonly Encoding StrictLatin1 = Encoding.GetEncoding("ISO-8859-1", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

	/// <summary>
	/// Gets a value indicating whether the file was not valid UTF-8 and had no byte order mark, so it
	/// was read byte-for-byte as Latin-1.
	/// </summary>
	internal bool IsLegacy => ReferenceEquals(Encoding, StrictLatin1);

	/// <summary>
	/// Reads a file, detecting its byte order mark, and falling back to Latin-1 when it is not valid UTF-8.
	/// </summary>
	/// <param name="path">The file to read.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The file's text and how it was stored.</returns>
	internal static async Task<EncodedText> ReadAsync(string path, CancellationToken ct = default)
	{
		byte[] bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
		return Decode(bytes);
	}

	/// <summary>
	/// Decodes raw file bytes, detecting the byte order mark, and falling back to Latin-1 when the
	/// bytes are not valid in the detected encoding.
	/// </summary>
	/// <param name="bytes">The file's bytes.</param>
	/// <returns>The text and how it was stored.</returns>
	internal static EncodedText Decode(byte[] bytes)
	{
		Ensure.NotNull(bytes);

		(Encoding encoding, int preambleLength) = DetectByteOrderMark(bytes);

		try
		{
			string text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
			return new EncodedText(text, encoding, bytes[..preambleLength]);
		}
		catch (DecoderFallbackException)
		{
			return new EncodedText(StrictLatin1.GetString(bytes), StrictLatin1, []);
		}
	}

	/// <summary>
	/// Encodes <paramref name="text"/> the way this file was stored, byte order mark included.
	/// </summary>
	/// <param name="text">The text to encode.</param>
	/// <param name="bytes">The encoded bytes, or empty when the text can't be represented.</param>
	/// <returns><see langword="true"/> when every character of the text can be written in this file's encoding.</returns>
	internal bool TryEncode(string text, out byte[] bytes)
	{
		Ensure.NotNull(text);

		try
		{
			bytes = [.. Preamble, .. Encoding.GetBytes(text)];
			return true;
		}
		catch (EncoderFallbackException)
		{
			bytes = [];
			return false;
		}
	}

	private static (Encoding Encoding, int PreambleLength) DetectByteOrderMark(byte[] bytes) => bytes switch
	{
		[0xFF, 0xFE, 0x00, 0x00, ..] => (StrictUtf32Le, 4),
		[0x00, 0x00, 0xFE, 0xFF, ..] => (StrictUtf32Be, 4),
		[0xEF, 0xBB, 0xBF, ..] => (StrictUtf8, 3),
		[0xFF, 0xFE, ..] => (StrictUtf16Le, 2),
		[0xFE, 0xFF, ..] => (StrictUtf16Be, 2),
		_ => (StrictUtf8, 0),
	};
}
