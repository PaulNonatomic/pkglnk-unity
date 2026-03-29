using System;
using UnityEngine;

namespace Nonatomic.PkgLnk.Editor.Utils
{
	/// <summary>
	/// Minimal GIF decoder that extracts the first frame as a Texture2D.
	/// Supports GIF87a/GIF89a, interlaced images, transparency,
	/// and both global and local color tables.
	/// </summary>
	public static class GifDecoder
	{
		private const int MaxLzwTableSize = 4096;

		/// <summary>
		/// Decodes the first frame of a GIF from raw bytes.
		/// Returns null if the data is not a valid GIF.
		/// </summary>
		public static Texture2D DecodeFirstFrame(byte[] data)
		{
			if (data == null || data.Length < 13) return null;

			// Validate header
			if (data[0] != 'G' || data[1] != 'I' || data[2] != 'F') return null;

			var pos = 6;

			// Logical Screen Descriptor
			var canvasWidth = ReadUInt16(data, ref pos);
			var canvasHeight = ReadUInt16(data, ref pos);
			if (canvasWidth == 0 || canvasHeight == 0) return null;

			var packed = data[pos++];
			var bgColorIndex = data[pos++];
			pos++; // pixel aspect ratio

			// Global Color Table
			var hasGct = (packed & 0x80) != 0;
			var gctEntries = hasGct ? 1 << ((packed & 0x07) + 1) : 0;
			byte[] globalColorTable = null;
			if (hasGct)
			{
				var gctBytes = gctEntries * 3;
				if (pos + gctBytes > data.Length) return null;
				globalColorTable = new byte[gctBytes];
				Buffer.BlockCopy(data, pos, globalColorTable, 0, gctBytes);
				pos += gctBytes;
			}

			// Scan for first image descriptor, reading graphic control extensions
			var transparentIndex = -1;

			while (pos < data.Length)
			{
				var blockType = data[pos++];

				if (blockType == 0x3B) break; // Trailer

				if (blockType == 0x21) // Extension
				{
					if (pos >= data.Length) return null;
					var extType = data[pos++];

					if (extType == 0xF9 && pos + 5 <= data.Length) // Graphic Control Extension
					{
						pos++; // block size (always 4)
						var gcPacked = data[pos++];
						pos += 2; // delay time
						var transIdx = data[pos++];
						pos++; // block terminator

						if ((gcPacked & 0x01) != 0)
							transparentIndex = transIdx;
					}
					else
					{
						SkipSubBlocks(data, ref pos);
					}

					continue;
				}

				if (blockType != 0x2C) continue; // Not an Image Descriptor

				// Image Descriptor
				if (pos + 9 > data.Length) return null;
				var imgLeft = ReadUInt16(data, ref pos);
				var imgTop = ReadUInt16(data, ref pos);
				var imgWidth = ReadUInt16(data, ref pos);
				var imgHeight = ReadUInt16(data, ref pos);
				if (imgWidth == 0 || imgHeight == 0) return null;

				var imgPacked = data[pos++];
				var hasLct = (imgPacked & 0x80) != 0;
				var interlaced = (imgPacked & 0x40) != 0;

				// Color table — local overrides global
				byte[] colorTable;
				if (hasLct)
				{
					var lctEntries = 1 << ((imgPacked & 0x07) + 1);
					var lctBytes = lctEntries * 3;
					if (pos + lctBytes > data.Length) return null;
					colorTable = new byte[lctBytes];
					Buffer.BlockCopy(data, pos, colorTable, 0, lctBytes);
					pos += lctBytes;
				}
				else
				{
					colorTable = globalColorTable;
				}

				if (colorTable == null) return null;

				// LZW minimum code size
				if (pos >= data.Length) return null;
				var minCodeSize = data[pos++];
				if (minCodeSize < 2 || minCodeSize > 11) return null;

				// Collect image data sub-blocks into contiguous array
				var imageData = CollectSubBlocks(data, ref pos);
				if (imageData == null || imageData.Length == 0) return null;

				// LZW decompress to index stream
				var pixelCount = imgWidth * imgHeight;
				var indices = DecompressLzw(imageData, minCodeSize, pixelCount);
				if (indices == null) return null;

				// Build RGBA pixel array
				var pixels = new Color32[canvasWidth * canvasHeight];

				// Fill canvas with background
				if (globalColorTable != null && bgColorIndex * 3 + 2 < globalColorTable.Length)
				{
					var bg = new Color32(
						globalColorTable[bgColorIndex * 3],
						globalColorTable[bgColorIndex * 3 + 1],
						globalColorTable[bgColorIndex * 3 + 2],
						255);
					for (var i = 0; i < pixels.Length; i++)
						pixels[i] = bg;
				}

				// Write frame pixels onto canvas
				var maxColorIndex = colorTable.Length / 3;
				for (var i = 0; i < indices.Length && i < pixelCount; i++)
				{
					var srcRow = interlaced ? DeinterlaceRow(i / imgWidth, imgHeight) : i / imgWidth;
					var srcCol = i % imgWidth;

					var idx = indices[i];
					if (idx == transparentIndex) continue;
					if (idx >= maxColorIndex) continue;

					var destX = imgLeft + srcCol;
					var destY = imgTop + srcRow;
					if (destX >= canvasWidth || destY >= canvasHeight) continue;

					// Texture2D is bottom-up, GIF is top-down
					var texY = canvasHeight - 1 - destY;
					pixels[texY * canvasWidth + destX] = new Color32(
						colorTable[idx * 3],
						colorTable[idx * 3 + 1],
						colorTable[idx * 3 + 2],
						255);
				}

				var texture = new Texture2D(canvasWidth, canvasHeight, TextureFormat.RGBA32, false)
				{
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp
				};
				texture.SetPixels32(pixels);
				texture.Apply();
				return texture;
			}

			return null;
		}

		private static ushort ReadUInt16(byte[] data, ref int pos)
		{
			var value = (ushort)(data[pos] | (data[pos + 1] << 8));
			pos += 2;
			return value;
		}

		private static void SkipSubBlocks(byte[] data, ref int pos)
		{
			while (pos < data.Length)
			{
				var size = data[pos++];
				if (size == 0) break;
				pos += size;
			}
		}

		private static byte[] CollectSubBlocks(byte[] data, ref int pos)
		{
			// First pass: calculate total size
			var totalSize = 0;
			var scanPos = pos;
			while (scanPos < data.Length)
			{
				var size = data[scanPos++];
				if (size == 0) break;
				totalSize += size;
				scanPos += size;
			}

			if (totalSize == 0) return null;

			// Second pass: copy data
			var result = new byte[totalSize];
			var destPos = 0;
			while (pos < data.Length)
			{
				var size = data[pos++];
				if (size == 0) break;
				var copyLen = Math.Min(size, data.Length - pos);
				Buffer.BlockCopy(data, pos, result, destPos, copyLen);
				pos += size;
				destPos += copyLen;
			}

			return result;
		}

		private static int[] DecompressLzw(byte[] data, int minCodeSize, int pixelCount)
		{
			var clearCode = 1 << minCodeSize;
			var eoiCode = clearCode + 1;

			var tablePrefix = new int[MaxLzwTableSize];
			var tableSuffix = new byte[MaxLzwTableSize];
			var tableLength = new int[MaxLzwTableSize];

			var output = new int[pixelCount];
			var outputPos = 0;

			// Temp buffer for outputting code sequences in reverse
			var tempBuffer = new byte[MaxLzwTableSize];

			var codeSize = minCodeSize + 1;
			var nextCode = eoiCode + 1;
			var codeMask = (1 << codeSize) - 1;

			// Initialize table with single-character entries
			for (var i = 0; i < clearCode; i++)
			{
				tablePrefix[i] = -1;
				tableSuffix[i] = (byte)i;
				tableLength[i] = 1;
			}

			var bitPos = 0;
			var prevCode = -1;
			var totalBits = data.Length * 8;

			while (bitPos + codeSize <= totalBits && outputPos < pixelCount)
			{
				var code = ReadBits(data, bitPos, codeSize) & codeMask;
				bitPos += codeSize;

				if (code == eoiCode) break;

				if (code == clearCode)
				{
					codeSize = minCodeSize + 1;
					nextCode = eoiCode + 1;
					codeMask = (1 << codeSize) - 1;
					prevCode = -1;
					continue;
				}

				int outputCode;

				if (code < nextCode)
				{
					outputCode = code;

					if (prevCode >= 0 && nextCode < MaxLzwTableSize)
					{
						tablePrefix[nextCode] = prevCode;
						tableSuffix[nextCode] = GetFirstByte(code, tablePrefix, tableSuffix);
						tableLength[nextCode] = tableLength[prevCode] + 1;
						nextCode++;
					}
				}
				else if (code == nextCode && prevCode >= 0)
				{
					// Special case: code equals next available
					var firstByte = GetFirstByte(prevCode, tablePrefix, tableSuffix);

					if (nextCode < MaxLzwTableSize)
					{
						tablePrefix[nextCode] = prevCode;
						tableSuffix[nextCode] = firstByte;
						tableLength[nextCode] = tableLength[prevCode] + 1;
						nextCode++;
					}

					outputCode = code;
				}
				else
				{
					break; // Invalid code
				}

				// Walk the chain backwards into temp buffer, then copy forward
				var len = tableLength[outputCode];
				var c = outputCode;
				for (var i = len - 1; i >= 0; i--)
				{
					tempBuffer[i] = tableSuffix[c];
					c = tablePrefix[c];
				}

				var remaining = pixelCount - outputPos;
				var copyLen = len < remaining ? len : remaining;
				for (var i = 0; i < copyLen; i++)
				{
					output[outputPos++] = tempBuffer[i];
				}

				prevCode = outputCode;

				// Grow code size when table exceeds current capacity
				if (nextCode > codeMask && codeSize < 12)
				{
					codeSize++;
					codeMask = (1 << codeSize) - 1;
				}
			}

			return outputPos > 0 ? output : null;
		}

		private static byte GetFirstByte(int code, int[] prefix, byte[] suffix)
		{
			while (prefix[code] >= 0)
				code = prefix[code];
			return suffix[code];
		}

		private static int ReadBits(byte[] data, int bitPos, int count)
		{
			var result = 0;
			for (var i = 0; i < count; i++)
			{
				var byteIndex = (bitPos + i) >> 3;
				var bitIndex = (bitPos + i) & 7;
				if (byteIndex >= data.Length) break;
				if ((data[byteIndex] & (1 << bitIndex)) != 0)
					result |= 1 << i;
			}

			return result;
		}

		/// <summary>
		/// Maps a sequential source row to the actual display row for interlaced GIFs.
		/// </summary>
		private static int DeinterlaceRow(int sourceRow, int height)
		{
			var row = 0;

			// Pass 1: every 8th row starting at 0
			for (var y = 0; y < height; y += 8)
			{
				if (row == sourceRow) return y;
				row++;
			}

			// Pass 2: every 8th row starting at 4
			for (var y = 4; y < height; y += 8)
			{
				if (row == sourceRow) return y;
				row++;
			}

			// Pass 3: every 4th row starting at 2
			for (var y = 2; y < height; y += 4)
			{
				if (row == sourceRow) return y;
				row++;
			}

			// Pass 4: every 2nd row starting at 1
			for (var y = 1; y < height; y += 2)
			{
				if (row == sourceRow) return y;
				row++;
			}

			return sourceRow;
		}
	}
}
