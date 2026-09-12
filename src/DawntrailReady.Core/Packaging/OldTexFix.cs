// Ports: TexTools Mods/FileTypes/TTMP.cs FixOldTexData and Mods/EndwalkerUpgrade.cs ValidateTexFileData /
// FastValidateTexFile, on top of section B's Tex.TexHeader (ReadTexHeader, ToBytes, FixUpBrokenMipOffsets).
using DawntrailReady.Core.Textures;

namespace DawntrailReady.Core.Packaging;

internal static class OldTexFix
{
    /// <summary>
    /// TTMP.FixOldTexData as the upgrader sees it: the uncompressed .tex after validation. TexTools then recompresses it
    /// into the unzipped .mpd (Tex.CompressTexFile), which is only its storage format. Throws where TexTools throws; the
    /// caller skips the file like TexTools does.
    /// </summary>
    public static byte[] FixOldTexData(byte[] uncompressed) => ValidateTexFileData(uncompressed) ?? uncompressed;

    /// <summary>EndwalkerUpgrade.ValidateTexFileData. Null when the file needs no change.</summary>
    public static byte[]? ValidateTexFileData(byte[] uncompressedTex)
    {
        using var ms = new MemoryStream(uncompressedTex);
        using var br = new BinaryReader(ms);
        var header = Tex.TexHeader.ReadTexHeader(br);
        if ((!IOUtil.IsPowerOfTwo(header.Width) || !IOUtil.IsPowerOfTwo(header.Height)) && header.MipCount > 1)
        {
            var tex = XivTex.FromUncompressedTex(uncompressedTex);
            // TexTools bug kept: the rounded WIDTH is used for both dimensions.
            Tex.ResizeXivTx(tex, IOUtil.RoundToPowerOfTwo(header.Width), IOUtil.RoundToPowerOfTwo(header.Width), false).GetAwaiter().GetResult();
            return tex.ToUncompressedTex();
        }

        // TexHeader is a struct: FixUpBrokenMipOffsets gets a copy, so its MipCount edit is lost while its array edits
        // (LoDMips, MipMapOffsets) reach header.ToBytes() below. Same as TexTools.
        var fixupResult = Tex.TexHeader.FixUpBrokenMipOffsets(header, uncompressedTex.Length);
        if (fixupResult.HeaderChanged || fixupResult.CalculatedTexSize != uncompressedTex.Length)
        {
            var newData = new byte[fixupResult.CalculatedTexSize];
            Array.Copy(header.ToBytes(), newData, Tex._TexHeaderSize);
            Array.Copy(uncompressedTex, Tex._TexHeaderSize, newData, Tex._TexHeaderSize, fixupResult.CalculatedTexSize - Tex._TexHeaderSize);
            return newData;
        }
        return null;
    }

    /// <summary>
    /// EndwalkerUpgrade.FastValidateTexFile: TexTools runs it on every .tex it unzips from a .pmp (ResolvePMPBasePath).
    /// Rewrites broken mip offsets in the header and cuts all-zero bytes past the last mip.
    /// </summary>
    public static bool FastValidateTexFile(string externalPath)
    {
        var repaired = false;
        var fi = new FileInfo(externalPath);
        using var fs = File.Open(externalPath, FileMode.Open, FileAccess.ReadWrite);
        var header = Tex.TexHeader.ReadTexHeader(new BinaryReader(fs, System.Text.Encoding.UTF8, true));
        var fixupResult = Tex.TexHeader.FixUpBrokenMipOffsets(header, fi.Length);

        // Rewrite the tex file header if the mip offsets were wrong
        if (fixupResult.HeaderChanged)
        {
            fs.Seek(0, SeekOrigin.Begin);
            fs.Write(header.ToBytes(), 0, (int)Tex._TexHeaderSize);
            repaired = true;
        }

        // "Textools would repeatedly add 80 null bytes to the end of textures"
        if (fixupResult.CalculatedTexSize < fi.Length)
        {
            var diff = fi.Length - fixupResult.CalculatedTexSize;
            var allZero = true;
            fs.Seek(fixupResult.CalculatedTexSize, SeekOrigin.Begin);
            for (long i = 0; i < diff; ++i)
            {
                if (fs.ReadByte() != 0)
                {
                    allZero = false;
                    break;
                }
            }

            if (allZero)
            {
                fs.SetLength(fixupResult.CalculatedTexSize);
                repaired = true;
            }
        }
        return repaired;
    }
}
