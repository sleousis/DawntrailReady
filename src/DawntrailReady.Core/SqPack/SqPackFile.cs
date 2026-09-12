// Port of xivModdingFramework SqPack/FileTypes/TransactionDataHandler.cs GetUncompressedFile(FileStorageInformation)
// (TexTools commit 25b3ae72), compressed branch, as reached for EFileStorageType.CompressedBlob: the TTMPD.mpd
// entries TTMP.cs and WizardData.cs describe with RealOffset = ModOffset and FileSize = ModSize.
using System.Text;

namespace DawntrailReady.Core.SqPack;

public static class SqPackFile
{
    /// <summary>
    /// Reads the SqPack file (type 2, 3 or 4) that starts at <paramref name="offset"/> in <paramref name="stream"/>
    /// and returns the uncompressed game file. The stream is left open.
    /// </summary>
    /// <param name="size">
    /// The entry's ModSize. TexTools records it as FileSize, but its compressed branch never reads it: the SqPack
    /// header alone decides how much is read. Accepted for the caller's convenience and ignored the same way.
    /// </param>
    public static byte[] Decompress(Stream stream, long offset, int size)
    {
        using var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        // Navigate to the offset, read and decompress the SQPack file.
        br.BaseStream.Seek(offset, SeekOrigin.Begin);
        return Dat.ReadSqPackFile(br, offset);
    }
}
