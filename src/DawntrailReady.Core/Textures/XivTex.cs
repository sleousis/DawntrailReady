// Port of xivModdingFramework Textures/DataContainers/XivTex.cs (TexTools, GPL-3.0).
// SaveAs (image export) is not ported; nothing in the upgrade uses it.

namespace DawntrailReady.Core.Textures;

/// <summary>
/// Class that represents an uncompressed .tex file.
/// </summary>
public class XivTex : ICloneable
{
    /// <summary>
    /// The Textures Format <see cref="XivTexFormat"/>
    /// </summary>
    public XivTexFormat TextureFormat { get; set; }

    /// <summary>
    /// The width of the texture
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// The height of the texture
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Number of layers in the in the texture file.
    /// </summary>
    public int Layers { get; set; }

    /// <summary>
    /// The amount of mipmaps the texture contains
    /// </summary>
    public int MipMapCount { get; set; }

    /// <summary>
    /// The texture byte data
    /// </summary>
    public byte[] TexData { get; set; } = [];

    /// <summary>
    /// The base path this texture originated from/is destined for.
    /// May or may not always be populated, depending on construction format.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Creates an XivTex object from the uncompressed bytes of a .TEX file.
    /// </summary>
    public static XivTex FromUncompressedTex(byte[] texData, int offset = 0)
    {
        using var ms = new MemoryStream(texData);
        using var br = new BinaryReader(ms);
        // TexTools passes the whole array length as the data size, whatever the offset.
        return FromUncompressedTex(br, texData.Length, offset);
    }

    /// <summary>
    /// Creates an XivTex object from the uncompressed bytes of a .TEX file.
    /// </summary>
    public static XivTex FromUncompressedTex(BinaryReader br, int dataSize, long offset = -1)
    {
        if (offset >= 0)
        {
            br.BaseStream.Seek(offset, SeekOrigin.Begin);
        }
        else
        {
            offset = br.BaseStream.Position;
        }

        var header = Tex.TexHeader.ReadTexHeader(br);

        var tex = new XivTex();

        tex.TextureFormat = Tex.TextureTypeDictionary[(int)header.TextureFormat];
        tex.Width = header.Width;
        tex.Height = header.Height;

        tex.Layers = header.ArraySize * header.Depth;
        tex.MipMapCount = header.MipCount;

        var mipDataLength = dataSize - (int)Tex._TexHeaderSize;
        var mipData = br.ReadBytes(mipDataLength);
        tex.TexData = mipData;

        return tex;
    }

    /// <summary>
    /// Converts this XivTex to uncompressed .TEX format bytes.
    /// Note: This conversion is lossy (at the header/metadata level).
    /// </summary>
    public byte[] ToUncompressedTex()
    {
        var header = Tex.CreateTexFileHeader(TextureFormat, Width, Height, MipMapCount);
        return header.Concat(TexData).ToArray();
    }

    /// <summary>
    /// Converts the DDS-Format pixel data in this XivTex to 8.8.8.8 RGBA Pixel data and returns it.
    /// </summary>
    public async Task<byte[]> GetRawPixels(int layer = -1)
    {
        var layers = Layers;
        if (layers == 0)
        {
            layers = 1;
        }
        return await DDS.ConvertPixelData(TexData, Width, Height, TextureFormat, layers, layer);
    }

    public object Clone()
    {
        var tex = (XivTex)MemberwiseClone();
        tex.TexData = (byte[])TexData.Clone();
        return tex;
    }
}
