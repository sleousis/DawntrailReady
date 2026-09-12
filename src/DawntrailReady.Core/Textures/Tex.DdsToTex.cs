// Port of xivModdingFramework Textures/FileTypes/Tex.cs (TexTools, GPL-3.0): DDSToUncompressedTex,
// GetDDSTexFormat, CreateTexFileHeader, DDSHeaderToTexHeader and TextureTypeDictionary.

namespace DawntrailReady.Core.Textures;

public static partial class Tex
{
    public static byte[] DDSToUncompressedTex(byte[] data)
    {
        var uncompressedLength = data.Length;
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        using var msOut = new MemoryStream();
        using var bw = new BinaryWriter(msOut);
        DDSToUncompressedTex(br, bw, (uint)uncompressedLength);
        return msOut.ToArray();
    }

    public static void DDSToUncompressedTex(BinaryReader br, BinaryWriter bw, uint ddsSize)
    {
        var uncompressedLength = ddsSize;
        var header = DDSHeaderToTexHeader(br);
        uncompressedLength -= header.DDSHeaderSize;
        bw.Write(header.TexHeader);
        bw.Write(br.ReadBytes((int)uncompressedLength));
    }

    /// <summary>
    /// Retrieves the texture format information from a DDS file stream/DDS header.
    /// Expects the stream position or offset to point to the start of the DDS Header.
    /// Advances the stream position somewhat arbitrarily.
    /// </summary>
    public static XivTexFormat GetDDSTexFormat(BinaryReader ddsStream, long offset = -1)
    {
        if (offset >= 0)
        {
            ddsStream.BaseStream.Seek(offset, SeekOrigin.Begin);
        }
        else
        {
            offset = ddsStream.BaseStream.Position;
        }

        ddsStream.BaseStream.Seek(offset + 12, SeekOrigin.Begin);

        var newHeight = ddsStream.ReadInt32();
        var newWidth = ddsStream.ReadInt32();
        ddsStream.ReadBytes(8);
        var newMipCount = ddsStream.ReadInt32();

        ddsStream.BaseStream.Seek(offset + DDS._DDS_PixelFormatOffset, SeekOrigin.Begin);

        var pixelFormatSize = ddsStream.ReadInt32();
        var flags = ddsStream.ReadUInt32();
        var fourCC = ddsStream.ReadUInt32();
        XivTexFormat textureType;

        if ((flags & DDS._DWFourCCFlag) != 0)
        {
            if (fourCC == DDS._DX10)
            {
                ddsStream.BaseStream.Seek(offset + 128, SeekOrigin.Begin);
                var dxgiTexType = ddsStream.ReadUInt32();

                if (DDS.DxgiTypeToXivTex.ContainsKey(dxgiTexType))
                {
                    textureType = DDS.DxgiTypeToXivTex[dxgiTexType];
                }
                else
                {
                    throw new Exception($"DXGI format ({dxgiTexType}) not recognized.");
                }
            }
            else if (DDS.DdsTypeToXivTex.ContainsKey(fourCC))
            {
                textureType = DDS.DdsTypeToXivTex[fourCC];
            }
            else
            {
                throw new Exception($"DDS Type ({fourCC}) not recognized.");
            }
        }
        else
        {
            // Uncompressed.
            textureType = XivTexFormat.A8R8G8B8;
        }

        switch (flags)
        {
            case 2 when textureType == XivTexFormat.A8R8G8B8:
                textureType = XivTexFormat.A8;
                break;
            case 65 when textureType == XivTexFormat.A8R8G8B8:
                var bpp = ddsStream.ReadInt32();
                if (bpp == 32)
                {
                    textureType = XivTexFormat.A8R8G8B8;
                }
                else
                {
                    var red = ddsStream.ReadInt32();

                    switch (red)
                    {
                        case 31744:
                            textureType = XivTexFormat.A1R5G5B5;
                            break;
                        case 3840:
                            textureType = XivTexFormat.A4R4G4B4;
                            break;
                    }
                }

                break;
        }
        return textureType;
    }

    /// <summary>
    /// Creates an uncompressed .TEX file header from the given data.
    /// (TexTools returns a List&lt;byte&gt;; the bytes are the same.)
    /// </summary>
    public static byte[] CreateTexFileHeader(XivTexFormat format, int newWidth, int newHeight, int newMipCount)
    {
        if (newMipCount > 13)
        {
            throw new InvalidDataException("Image has too many MipMaps. (Max 13)");
        }
        var headerData = new List<byte>();

        headerData.AddRange(BitConverter.GetBytes((short)0));
        headerData.AddRange(BitConverter.GetBytes((short)128));
        headerData.AddRange(BitConverter.GetBytes(short.Parse(format.GetTexFormatCode())));
        headerData.AddRange(BitConverter.GetBytes((short)0));
        headerData.AddRange(BitConverter.GetBytes((short)newWidth));
        headerData.AddRange(BitConverter.GetBytes((short)newHeight));
        headerData.AddRange(BitConverter.GetBytes((short)1));
        headerData.AddRange(BitConverter.GetBytes((short)newMipCount));

        var mipSizes = DDS.CalculateMipMapSizes(format, newWidth, newHeight);

        if (mipSizes.Count < newMipCount)
            throw new InvalidDataException($"CreateTexFileHeader: newMipCount ({newMipCount}) is too high for texture ({newWidth}x{newHeight}, format={format})");

        headerData.AddRange(BitConverter.GetBytes(0)); // LoD 0 Mip
        headerData.AddRange(BitConverter.GetBytes(newMipCount > 1 ? 1 : 0)); // LoD 1 Mip
        headerData.AddRange(BitConverter.GetBytes(newMipCount > 2 ? 2 : (newMipCount - 1))); // LoD 2 Mip

        var mipMapUncompressedOffset = 80;

        for (var i = 0; i < newMipCount; i++)
        {
            headerData.AddRange(BitConverter.GetBytes(mipMapUncompressedOffset));
            mipMapUncompressedOffset = mipMapUncompressedOffset + mipSizes[i];
        }

        var padding = 80 - headerData.Count;

        headerData.AddRange(new byte[padding]);

        return headerData.ToArray();
    }

    /// <summary>
    /// Replaces the DDS File header with a TexFile header.
    /// Reads the incoming Binary Reader stream to the end of the DDS Header.
    /// </summary>
    public static (byte[] TexHeader, uint DDSHeaderSize) DDSHeaderToTexHeader(BinaryReader br, long offset = -1)
    {
        if (offset >= 0)
        {
            br.BaseStream.Seek(offset, SeekOrigin.Begin);
        }
        else
        {
            offset = br.BaseStream.Position;
        }

        // DDS Header Reference: https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header
        var texFormat = GetDDSTexFormat(br, offset);

        br.BaseStream.Seek(offset + 4, SeekOrigin.Begin);
        var flags = br.ReadInt32();

        br.BaseStream.Seek(offset + 12, SeekOrigin.Begin);

        var newHeight = br.ReadInt32();
        var newWidth = br.ReadInt32();
        br.ReadBytes(8);
        var newMipCount = br.ReadInt32();

        if ((!IOUtil.IsPowerOfTwo(newHeight) || !IOUtil.IsPowerOfTwo(newWidth)) && newMipCount > 1)
        {
            throw new Exception("Resolution must be a multiple of 2.  (Ex. 256, 512, 1024, ...)");
        }

        if (offset >= 0)
        {
            br.BaseStream.Seek(offset, SeekOrigin.Begin);
        }

        br.BaseStream.Seek(offset + DDS._DDS_PixelFormatOffset, SeekOrigin.Begin);
        var pixfmtSize = br.ReadUInt32();
        var pixfmtFlags = br.ReadUInt32();
        var dwFourCc = br.ReadUInt32();

        var headerLength = _DDSHeaderSize;

        if ((pixfmtFlags & DDS._DWFourCCFlag) != 0)
        {
            if (dwFourCc == DDS._DX10)
            {
                headerLength += 20;
            }
        }

        // TexTools seeks to the header length from the start of the stream, not from offset.
        br.BaseStream.Seek(headerLength, SeekOrigin.Begin);

        // Write header.
        var texHeader = CreateTexFileHeader(texFormat, newWidth, newHeight, newMipCount);
        return (texHeader, headerLength);
    }

    /// <summary>
    /// Dictionary that holds [Texture Code, Texture Format] data
    /// </summary>
    public static readonly Dictionary<int, XivTexFormat> TextureTypeDictionary = new()
    {
        { 4400, XivTexFormat.L8 },
        { 4401, XivTexFormat.A8 },
        { 5184, XivTexFormat.A4R4G4B4 },
        { 5185, XivTexFormat.A1R5G5B5 },
        { 5200, XivTexFormat.A8R8G8B8 },
        { 5201, XivTexFormat.X8R8G8B8 },
        { 8528, XivTexFormat.R32F },
        { 8784, XivTexFormat.G16R16F },
        { 8800, XivTexFormat.G32R32F },
        { 9312, XivTexFormat.A16B16G16R16F },
        { 9328, XivTexFormat.A32B32G32R32F },
        { 13344, XivTexFormat.DXT1 },
        { 13360, XivTexFormat.DXT3 },
        { 13361, XivTexFormat.DXT5 },
        { 16704, XivTexFormat.D16 },
        { 24864, XivTexFormat.BC4 },
        { 25136, XivTexFormat.BC5 },
        { 25650, XivTexFormat.BC7 },
    };
}
