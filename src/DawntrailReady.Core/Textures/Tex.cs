// Port of xivModdingFramework Textures/FileTypes/Tex.cs (TexTools, GPL-3.0): TexHeader, ResizeXivTx,
// MergePixelData, GetMipCount, GetCompressionFormat, ConvertToDDS(byte[]...) and CreateFast8888DDS.
// The DDS -> .tex half lives in Tex.DdsToTex.cs.

using System.Diagnostics;
using System.Text;

namespace DawntrailReady.Core.Textures;

/// <summary>
/// The subset of TeximpNet's CompressionFormat that TexTools' GetCompressionFormat can return.
/// </summary>
public enum CompressionFormat
{
    BGRA,
    BC1a,
    BC3,
    BC4,
    BC5,
    BC7,
}

/// <summary>
/// This class contains the methods that deal with the .tex file type
/// </summary>
public static partial class Tex
{
    public const uint _DDSHeaderSize = 128;
    public const uint _TexHeaderSize = 80;

    public struct TexHeader
    {
        // Bitflags
        public uint Attributes;

        // Texture Format
        public uint TextureFormat;

        public ushort Width;
        public ushort Height;

        public ushort Depth;

        public byte MipCount;
        public byte MipFlag;

        public byte ArraySize;

        // 3 Ints, representing which MipMaps to use at each LoD level.
        public uint[] LoDMips;

        public uint[] MipMapOffsets;

        /// <summary>
        /// Reads a .tex file header (80 bytes) from the given stream.
        /// </summary>
        public static TexHeader ReadTexHeader(BinaryReader br, long offset = -1)
        {
            var header = new TexHeader();
            if (offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }

            header.Attributes = br.ReadUInt32();
            header.TextureFormat = br.ReadUInt32();

            header.Width = br.ReadUInt16();
            header.Height = br.ReadUInt16();

            header.Depth = br.ReadUInt16();
            header.MipCount = br.ReadByte();
            header.MipFlag = (byte)(header.MipCount >> 4);
            header.MipCount = (byte)(header.MipCount & 0xF);
            header.ArraySize = br.ReadByte();

            header.LoDMips = new uint[3];
            for (int i = 0; i < header.LoDMips.Length; i++)
            {
                header.LoDMips[i] = br.ReadUInt32();
            }

            header.MipMapOffsets = new uint[13];
            for (int i = 0; i < header.MipMapOffsets.Length; i++)
            {
                header.MipMapOffsets[i] = br.ReadUInt32();
            }
            return header;
        }

        /// <summary>
        /// Writes a .tex file header from this.
        /// </summary>
        public byte[] ToBytes()
        {
            var res = new byte[_TexHeaderSize];
            var bw = new BinaryWriter(new MemoryStream(res, true));
            bw.Write(Attributes);
            bw.Write(TextureFormat);
            bw.Write(Width);
            bw.Write(Height);
            bw.Write(Depth);
            bw.Write((byte)((MipFlag << 4) | MipCount));
            bw.Write(ArraySize);
            foreach (var x in LoDMips)
                bw.Write(x);
            foreach (var x in MipMapOffsets)
                bw.Write(x);

            Debug.Assert(bw.BaseStream.Position == _TexHeaderSize, "Data was not fully written.");
            return res;
        }

        // Many tex files were written with broken mipmap offsets, and extra data at the end.
        // Try to rebuild them here, using the total tex file size as a heuristic.
        // Returns a flag indicating if any data was changed, as well as the size in bytes that the tex file should be.
        // As in TexTools the header is a struct passed by value: MipCount changes stay local, while the
        // LoDMips/MipMapOffsets arrays are shared with the caller and are modified in place.
        public static (bool HeaderChanged, long CalculatedTexSize) FixUpBrokenMipOffsets(TexHeader header, long texSizeIncludingHeader)
        {
            bool modified = false;
            int originalMipCount = header.MipCount;
            int mipOffset = (int)_TexHeaderSize;

            // A mip count of more than 13 is impossible
            if (originalMipCount > 13)
                originalMipCount = 13;

            // This will throw for unknown formats
            var mipSizes = DDS.CalculateMipMapSizes((XivTexFormat)header.TextureFormat, header.Width, header.Height);

            // Ensure MipCount is always something valid. First offset will always be 80 or 0x50
            header.MipCount = 1;
            modified |= (header.MipMapOffsets[0] != (uint)mipOffset);
            header.MipMapOffsets[0] = (uint)mipOffset;
            mipOffset += mipSizes[0];

            int mipLevel;

            for (mipLevel = 1; mipLevel < originalMipCount; ++mipLevel)
            {
                // There's more mipmaps in the original file than we calculated should be possible -- cut the list short
                if (mipLevel >= mipSizes.Count)
                    break;

                int mipSize = mipSizes[mipLevel];

                // We've reached a mipmap we calculate would extend past the end of the file -- cut the list short
                if (mipOffset + mipSize > texSizeIncludingHeader)
                    break;

                // Write the expected mipmap offset
                modified |= (header.MipMapOffsets[mipLevel] != (uint)mipOffset);
                header.MipMapOffsets[mipLevel] = (uint)mipOffset;

                // Next offset
                mipOffset += mipSize;

                // Bump the mip count in the header to match what we have verified so far
                header.MipCount = (byte)(mipLevel + 1);
            }

            uint maxLodMip = 0;
            // Update LoDMips in case we removed a referenced mipmap
            // ... or if the values are not correctly in ascending order
            for (int lodLevel = 0; lodLevel < 3; ++lodLevel)
            {
                if (header.LoDMips[lodLevel] >= header.MipCount)
                {
                    modified = true;
                    header.LoDMips[lodLevel] = (uint)(header.MipCount - 1);
                }
                if (header.LoDMips[lodLevel] < maxLodMip)
                {
                    modified = true;
                    header.LoDMips[lodLevel] = maxLodMip;
                }
                maxLodMip = header.LoDMips[lodLevel];
            }

            // Fill out the rest of table with zeroes
            for (; mipLevel < 13; ++mipLevel)
            {
                if (header.MipMapOffsets[mipLevel] != 0)
                {
                    modified = true;
                    header.MipMapOffsets[mipLevel] = 0;
                }
            }

            modified |= (header.MipCount != originalMipCount);

            return (modified, mipOffset);
        }
    }

    public static async Task ResizeXivTx(XivTex tex, int width, int height, bool nearestNeighbor = false)
    {
        var data = await TextureHelpers.ResizeImage(tex, width, height, nearestNeighbor);

        tex.Height = height;
        tex.Width = width;
        await MergePixelData(tex, data);
    }

    public static Task MergePixelData(XivTex tex, byte[] data)
    {
        // Always retain mip settings.
        bool useMips = tex.MipMapCount > 1 ? true : false;

        CompressionFormat compressionFormat = GetCompressionFormat(tex.TextureFormat);

        if (compressionFormat == CompressionFormat.BGRA)
        {
            tex.TextureFormat = XivTexFormat.A8R8G8B8;
        }

        if (compressionFormat != CompressionFormat.BC7)
        {
            if (tex.Width < 64 || tex.Height < 64)
            {
                // The TexImpNet compressor will hard crash the entire application with a memory error with small sizes.
                throw new InvalidDataException("Image is too small for DDS Compressor. (64x64 Minimum Size)");
            }
        }

        // NOT PORTED: TexTools re-encodes the pixels here with native encoders (texconv.exe for BC7, TeximpNet/nvtt
        // for everything else) in the texture's own format, with nvtt/texconv generated mipmaps. Neither encoder can
        // be reproduced bit for bit, so the pixels are stored uncompressed as A8R8G8B8 instead (B,G,R,A bytes, the
        // layout TexTools' 8888 textures use). For textures that were already A8R8G8B8 the top mip is what TexTools
        // produces; for block-compressed sources TexTools' extra compression loss is absent and the format changes to
        // A8R8G8B8. The lower mips are nearest-pixel picks, not nvtt's filtered mips.
        tex.TextureFormat = XivTexFormat.A8R8G8B8;

        var mipCount = useMips ? GetMipCount(tex.Width, tex.Height) : Math.Max(1, tex.MipMapCount);
        tex.TexData = CreateUncompressed8888Mips(data, tex.Width, tex.Height, mipCount);

        if (useMips)
        {
            var calc = GetMipCount(tex.Width, tex.Height);
            tex.MipMapCount = calc;
        }
        return Task.CompletedTask;
    }

    // Stand-in for the native encoders in MergePixelData (see the NOT PORTED note there).
    private static byte[] CreateUncompressed8888Mips(byte[] rgbaData, int width, int height, int mipCount)
    {
        var mipSizes = DDS.CalculateMipMapSizes(XivTexFormat.A8R8G8B8, width, height);
        mipCount = Math.Min(mipCount, mipSizes.Count);

        var total = 0;
        for (int i = 0; i < mipCount; i++)
        {
            total += mipSizes[i];
        }

        var ret = new byte[total];
        for (int i = 0; i < width * height * 4; i += 4)
        {
            ret[i + 0] = rgbaData[i + 2];
            ret[i + 1] = rgbaData[i + 1];
            ret[i + 2] = rgbaData[i + 0];
            ret[i + 3] = rgbaData[i + 3];
        }

        var lastOffset = 0;
        var offset = mipSizes[0];
        int lastW = width, lastH = height;
        for (int m = 1; m < mipCount; m++)
        {
            var w = Math.Max(1, lastW / 2);
            var h = Math.Max(1, lastH / 2);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var sx = Math.Min(x * 2, lastW - 1);
                    var sy = Math.Min(y * 2, lastH - 1);
                    Array.Copy(ret, lastOffset + ((sy * lastW) + sx) * 4, ret, offset + ((y * w) + x) * 4, 4);
                }
            }
            lastOffset = offset;
            offset += mipSizes[m];
            lastW = w;
            lastH = h;
        }
        return ret;
    }

    public static int GetMipCount(int width, int height)
    {
        return GetMipCount(width > height ? width : height);
    }

    public static int GetMipCount(int largestSize)
    {
        return (int)Math.Floor(Math.Log(largestSize, 2) + 1);
    }

    public static CompressionFormat GetCompressionFormat(XivTexFormat format)
    {
        // Ensure we're converting to a format we can actually process.
        CompressionFormat compressionFormat;
        switch (format)
        {
            case XivTexFormat.DXT1:
                compressionFormat = CompressionFormat.BC1a;
                break;
            case XivTexFormat.DXT5:
                compressionFormat = CompressionFormat.BC3;
                break;
            case XivTexFormat.BC4:
                compressionFormat = CompressionFormat.BC4;
                break;
            case XivTexFormat.BC5:
                compressionFormat = CompressionFormat.BC5;
                break;
            case XivTexFormat.BC7:
                compressionFormat = CompressionFormat.BC7;
                break;
            case XivTexFormat.A8R8G8B8:
                compressionFormat = CompressionFormat.BGRA;
                break;
            default:
                throw new InvalidDataException("Format is currently unsupported: " + format.ToString());
        }

        return compressionFormat;
    }

    /// <summary>
    /// Returns the raw bytes of a DDS file.
    /// Note: like TexTools, this swizzles <paramref name="rgbaData"/> to BGRA in place.
    /// </summary>
    /// <param name="rgbaData">8.8.8.8 Pixel format data.</param>
    public static async Task<byte[]> ConvertToDDS(byte[] rgbaData, XivTexFormat texFormat, bool useMipMaps, int width, int height, bool allowFast8888 = true)
    {
        // Ensure we're converting to a format we can actually process.
        CompressionFormat compressionFormat = GetCompressionFormat(texFormat);

        if (compressionFormat == CompressionFormat.BC7)
        {
            // NOT PORTED: TexTools runs texconv.exe (BC7_UNORM) here.
            throw new NotSupportedException("BC7 encoding goes through texconv.exe in TexTools and is not ported.");
        }

        await TextureHelpers.SwizzleRB(rgbaData, width, height);
        if (allowFast8888 && texFormat == XivTexFormat.A8R8G8B8)
        {
            return CreateFast8888DDS(rgbaData, width, height);
        }

        // NOT PORTED: TexTools runs the native TeximpNet (nvtt) compressor here, with up to 13 mips when useMipMaps.
        throw new NotSupportedException($"{texFormat} encoding goes through TeximpNet in TexTools and is not ported.");
    }

    /// <summary>
    /// Creates a valid DDS file from a 8.8.8.8 byte array...
    /// By manually creating a DDS header and stapling it onto the end.
    /// The quality of the MipMaps it generates is quite bad though.
    /// </summary>
    private static byte[] CreateFast8888DDS(byte[] data, int width, int height)
    {
        var header = new byte[128];
        var pixelSizeBits = 32;

        var minDim = Math.Min(height, width);

        // Minimum size mipmap we care about is 32x32, to simplify things.
        var mipCount = (int)Math.Log(minDim, 2);
        mipCount = Math.Max(mipCount, 1);

        Encoding.ASCII.GetBytes("DDS ").CopyTo(header, 0);
        BitConverter.GetBytes(124).CopyTo(header, 4);

        // Flags?
        BitConverter.GetBytes(0x21007).CopyTo(header, 8);

        // Size
        BitConverter.GetBytes(height).CopyTo(header, 12);
        BitConverter.GetBytes(width).CopyTo(header, 16);

        // Pitch
        BitConverter.GetBytes(width * 4).CopyTo(header, 20);

        // Depth
        BitConverter.GetBytes(0).CopyTo(header, 24);

        // MipMap Count
        BitConverter.GetBytes(mipCount).CopyTo(header, 28);

        // dwCaps. DDSCAPS_MIPMAP(0x40000) + DDSCAPS_TEXTURE(0x1000)
        BitConverter.GetBytes(mipCount > 1 ? 0x401000 : 0x1000).CopyTo(header, 104);

        var startOfPixStruct = 76;
        // Pixel struct size
        BitConverter.GetBytes(32).CopyTo(header, startOfPixStruct);

        // Pixel Flags.  In this case, uncompressed(0x40) + contains alpha(0x01).
        BitConverter.GetBytes(0x41).CopyTo(header, startOfPixStruct + 4);

        // DWFourCC, unused
        BitConverter.GetBytes(0).CopyTo(header, startOfPixStruct + 8);

        // Pixel size
        BitConverter.GetBytes(pixelSizeBits).CopyTo(header, startOfPixStruct + 12);

        // Red Mask
        BitConverter.GetBytes(0x00ff0000).CopyTo(header, startOfPixStruct + 16);

        // Green Mask
        BitConverter.GetBytes(0x0000ff00).CopyTo(header, startOfPixStruct + 20);

        // Blue Mask
        BitConverter.GetBytes(0x000000ff).CopyTo(header, startOfPixStruct + 24);

        // Alpha Mask
        BitConverter.GetBytes(0xff000000).CopyTo(header, startOfPixStruct + 28);

        var pixelSize = pixelSizeBits / 8;

        var lastMipData = data;
        var currentMipSize = data.Length;
        var totalMipSize = data.Length;
        var curw = width;
        var curh = height;

        var mipData = new List<byte[]>(mipCount);
        mipData.Add(data);
        for (int i = 1; i < mipCount; i++)
        {
            // Each MipMap is 1/4 the net size of the last.
            currentMipSize /= 4;
            curw /= 2;
            curh /= 2;

            var mipArray = new byte[currentMipSize];

            // We are about to compute the world's singularly worst MipMaps in existence.
            // But it's going to be fast.
            for (int y = 0; y < curh; y++)
            {
                for (int x = 0; x < curw; x++)
                {
                    var destOffset = ((y * curw) + x) * pixelSize;
                    var sourceOffset = (((y * 2) * (curw * 2)) + (x * 2)) * pixelSize;

                    // Copy one of the pixels into the mip data.
                    Array.Copy(lastMipData, sourceOffset, mipArray, destOffset, pixelSize);
                }
            }
            mipData.Add(mipArray);
            lastMipData = mipArray;
            totalMipSize += mipArray.Length;
        }

        // Allocate final array and copy the data in.
        var ret = new byte[header.Length + totalMipSize];
        header.CopyTo(ret, 0);
        var offset = header.Length;
        for (int i = 0; i < mipCount; i++)
        {
            mipData[i].CopyTo(ret, offset);
            offset += mipData[i].Length;
        }

        // And just like that, we have the world's worst Mip-Enabled DDS file.
        return ret;
    }
}
