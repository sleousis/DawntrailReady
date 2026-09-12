// Port of xivModdingFramework SqPack/FileTypes/Dat.cs (TexTools commit 25b3ae72), region "SQPack Compressed File
// Reading/Decompressing": ReadSqPackType2, ReadSqPackType3, ReadSqPackType4, GetSqPackType, ReadSqPackFile.
// The block helpers they call are in Dat.Blocks.cs.
//
// TexTools' readers are async and inflate blocks on Task.Run. These are synchronous: blocks are still read from the
// stream in the same order in BeginReadCompressedBlocks and inflated, in order, in CompleteReadCompressedBlocks, so
// the output bytes and the order in which errors surface are the same.
namespace DawntrailReady.Core.SqPack;

public static partial class Dat
{
    // Tex._TexHeaderSize in TexTools' Textures/FileTypes/Tex.cs.
    private const uint _TexHeaderSize = 80;

    public static byte[] ReadSqPackType2(byte[] data, long offset = 0)
    {
        using (var ms = new MemoryStream(data))
        {
            using (var br = new BinaryReader(ms))
            {
                return ReadSqPackType2(br, offset);
            }
        }
    }

    public static byte[] ReadSqPackType2(BinaryReader br, long offset = -1)
    {
        if (offset >= 0)
        {
            br.BaseStream.Seek(offset, SeekOrigin.Begin);
        }
        else
        {
            offset = br.BaseStream.Position;
        }

        var headerLength = br.ReadInt32();
        var fileType = br.ReadInt32();
        if (fileType != 2)
        {
            throw new Exception("Requested Type 2 file is not a valid type 2 file.");
        }
        var uncompSize = br.ReadInt32();
        var bufferInfoA = br.ReadInt32();
        var bufferInfoB = br.ReadInt32();

        var dataBlockCount = br.ReadInt32();

        var type2Bytes = new List<byte>(uncompSize);
        for (var i = 0; i < dataBlockCount; i++)
        {
            br.BaseStream.Seek(offset + (24 + (8 * i)), SeekOrigin.Begin);

            var dataBlockOffset = br.ReadInt32();

            br.BaseStream.Seek(offset + headerLength + dataBlockOffset, SeekOrigin.Begin);

            br.ReadBytes(8);

            var compressedSize = br.ReadInt32();
            var uncompressedSize = br.ReadInt32();

            // When the compressed size of a data block shows 32000, it is uncompressed.
            if (compressedSize == 32000)
            {
                type2Bytes.AddRange(br.ReadBytes(uncompressedSize));
            }
            else
            {
                var compressedData = br.ReadBytes(compressedSize);

                var decompressedData = Decompressor(compressedData, uncompressedSize);

                type2Bytes.AddRange(decompressedData);
            }
        }
        return type2Bytes.ToArray();
    }

    public static byte[] ReadSqPackType3(byte[] data)
    {
        using (var ms = new MemoryStream(data))
        {
            using (var br = new BinaryReader(ms))
            {
                return ReadSqPackType3(br);
            }
        }
    }

    public static byte[] ReadSqPackType3(BinaryReader br, long offset = -1)
    {
        if (offset >= 0)
        {
            br.BaseStream.Seek(offset, SeekOrigin.Begin);
        }
        else
        {
            offset = br.BaseStream.Position;
        }

        const int baseHeaderLength = 68; // start of file until after "padding"
        var headerLength = br.ReadInt32();
        var fileType = br.ReadInt32();
        var decompressedSize = br.ReadInt32();
        var buffer1 = br.ReadInt32();
        var buffer2 = br.ReadInt32();
        var version = br.ReadInt32();

        var endOfHeader = offset + headerLength;

        // Uncompressed...
        var vertexInfoSize = br.ReadInt32();
        var modelDataSize = br.ReadInt32();
        var vertexBufferSizes = Read3IntBuffer(br);
        var edgeGeometryVertexBufferSizes = Read3IntBuffer(br);
        var indexBufferSizes = Read3IntBuffer(br);

        // Compressed...
        var vertexInfoCompressedSize = br.ReadInt32();
        var modelDataCompressedSize = br.ReadInt32();
        var compressedvertexBufferSizes = Read3IntBuffer(br);
        var compressededgeGeometryVertexBufferSizes = Read3IntBuffer(br);
        var compressedindexBufferSizes = Read3IntBuffer(br);

        // Offsets....
        var vertexInfoOffset = br.ReadInt32();
        var modelDataOffset = br.ReadInt32();
        var vertexBufferOffsets = Read3IntBuffer(br);
        var edgeGeometryVertexBufferOffsets = Read3IntBuffer(br);
        var indexBufferOffsets = Read3IntBuffer(br);

        // Block Indexes....
        var vertexInfoBlockIndex = br.ReadInt16();
        var modelDataBLockIndex = br.ReadInt16();
        var vertexBufferBlockIndexs = Read3IntBuffer(br, true);
        var edgeGeometryVertexBufferBlockIndexs = Read3IntBuffer(br, true);
        var indexBufferBlockIndexs = Read3IntBuffer(br, true);

        // Block Counts....
        var vertexInfoBlockCount = br.ReadInt16();
        var modelDataBlockCount = br.ReadInt16();
        var vertexBufferBlockCounts = Read3IntBuffer(br, true);
        var edgeGeometryVertexBufferBlockCounts = Read3IntBuffer(br, true);
        var indexBufferBlockCounts = Read3IntBuffer(br, true);

        var meshCount = br.ReadUInt16();
        var materialCount = br.ReadUInt16();

        var lodCount = br.ReadByte();
        var flags = br.ReadByte();

        var padding = br.ReadBytes(2);

        var totalBlocks = vertexInfoBlockCount + modelDataBlockCount;
        totalBlocks += vertexBufferBlockCounts.Sum(x => (int)x);
        totalBlocks += edgeGeometryVertexBufferBlockCounts.Sum(x => (int)x);
        totalBlocks += indexBufferBlockCounts.Sum(x => (int)x);

        var blockSizes = new int[totalBlocks];

        for (var i = 0; i < totalBlocks; i++)
        {
            blockSizes[i] = br.ReadUInt16();
        }

        var distanceToEndOfHeader = endOfHeader - br.BaseStream.Position;
        var extraData = br.ReadBytes((int)distanceToEndOfHeader);

        // Read the compressed blocks.
        // These could technically be read as contiguous blocks typically,
        // But it's safer to actually use their offsets and validate them in the process.

        var vertexInfoData = BeginReadCompressedBlocks(br, vertexInfoBlockCount, endOfHeader + vertexInfoOffset);
        var modelInfoData = BeginReadCompressedBlocks(br, modelDataBlockCount, endOfHeader + modelDataOffset);

        const int _VertexSegments = 3;
        var vertexBuffers = new List<Func<byte[]>>[_VertexSegments];
        for (int i = 0; i < _VertexSegments; i++)
        {
            vertexBuffers[i] = BeginReadCompressedBlocks(br, (int)vertexBufferBlockCounts[i], endOfHeader + vertexBufferOffsets[i]);
        }

        var edgeBuffers = new List<Func<byte[]>>[_VertexSegments];
        for (int i = 0; i < _VertexSegments; i++)
        {
            edgeBuffers[i] = BeginReadCompressedBlocks(br, (int)edgeGeometryVertexBufferBlockCounts[i], endOfHeader + edgeGeometryVertexBufferOffsets[i]);
        }

        var indexBuffers = new List<Func<byte[]>>[_VertexSegments];
        for (int i = 0; i < _VertexSegments; i++)
        {
            var last = false;
            if (i == _VertexSegments - 1)
            {
                last = true;
            }
            indexBuffers[i] = BeginReadCompressedBlocks(br, (int)indexBufferBlockCounts[i], endOfHeader + indexBufferOffsets[i], last);
        }

        // Reserve space at the start of the result for the header
        var decompressedData = new byte[baseHeaderLength + decompressedSize];
        int decompOffset = baseHeaderLength;

        // Need to mark these as we unzip them.
        var vertexBufferUncompressedOffsets = new uint[_VertexSegments];
        var indexBufferUncompressedOffsets = new uint[_VertexSegments];
        var vertexBufferRealSizes = new uint[_VertexSegments];
        var indexBufferRealSizes = new uint[_VertexSegments];

        // Vertex and Model Headers
        var res = CompleteReadCompressedBlocks(vertexInfoData, decompressedData, decompOffset);
        decompressedData = res.Buffer;
        decompOffset += res.BytesWritten;
        var vInfoRealSize = res.BytesWritten;

        res = CompleteReadCompressedBlocks(modelInfoData, decompressedData, decompOffset);
        decompressedData = res.Buffer;
        decompOffset += res.BytesWritten;
        var mInfoRealSize = res.BytesWritten;

        for (int i = 0; i < _VertexSegments; i++)
        {
            // Geometry data in LoD order.
            // Mark the real uncompressed offsets and sizes on the way through.
            vertexBufferUncompressedOffsets[i] = (uint)decompOffset;
            res = CompleteReadCompressedBlocks(vertexBuffers[i], decompressedData, decompOffset);
            decompressedData = res.Buffer;
            decompOffset += res.BytesWritten;
            vertexBufferRealSizes[i] = (uint)res.BytesWritten;

            res = CompleteReadCompressedBlocks(edgeBuffers[i], decompressedData, decompOffset);
            decompressedData = res.Buffer;
            decompOffset += res.BytesWritten;

            indexBufferUncompressedOffsets[i] = (uint)decompOffset;
            res = CompleteReadCompressedBlocks(indexBuffers[i], decompressedData, decompOffset);
            decompressedData = res.Buffer;
            decompOffset += res.BytesWritten;
            indexBufferRealSizes[i] = (uint)res.BytesWritten;
        }

        var header = new List<byte>(baseHeaderLength);

        // Generated header for live/uncompressed MDL files.
        header.AddRange(BitConverter.GetBytes(version));
        header.AddRange(BitConverter.GetBytes(vInfoRealSize));
        header.AddRange(BitConverter.GetBytes(mInfoRealSize));
        header.AddRange(BitConverter.GetBytes((ushort)meshCount));
        header.AddRange(BitConverter.GetBytes((ushort)materialCount));

        Write3IntBuffer(header, vertexBufferUncompressedOffsets);
        Write3IntBuffer(header, indexBufferUncompressedOffsets);
        Write3IntBuffer(header, vertexBufferRealSizes);
        Write3IntBuffer(header, indexBufferRealSizes);

        header.Add(lodCount);
        header.Add(flags);
        header.AddRange(padding);

        // Copy the header over the reserved space at the start of decompressedData
        header.CopyTo(decompressedData, 0);

        return decompressedData;
    }

    public static byte[] ReadSqPackType4(byte[] data)
    {
        using (var ms = new MemoryStream(data))
        {
            using (var br = new BinaryReader(ms))
            {
                return ReadSqPackType4(br);
            }
        }
    }

    public static byte[] ReadSqPackType4(BinaryReader br, long offset = -1)
    {
        if (offset >= 0)
        {
            br.BaseStream.Seek(offset, SeekOrigin.Begin);
        }
        else
        {
            offset = br.BaseStream.Position;
        }

        // Standard SQPack header.
        var headerLength = br.ReadInt32();
        var fileType = br.ReadInt32();
        var uncompressedFileSize = br.ReadInt32();
        var ikd1 = br.ReadInt32();
        var ikd2 = br.ReadInt32();

        // Count of mipmaps.
        var mipCount = br.ReadInt32();

        var endOfHeader = offset + headerLength;
        var mipMapInfoOffset = offset + 24;

        // Tex File Header
        br.BaseStream.Seek(endOfHeader, SeekOrigin.Begin);
        var texHeader = br.ReadBytes((int)_TexHeaderSize);

        // Decompress Mipmap blocks ...
        var decompressedData = new byte[uncompressedFileSize];
        Array.Copy(texHeader, 0, decompressedData, 0, texHeader.Length);
        int decompOffset = texHeader.Length;

        var mipData = new List<Func<byte[]>>[mipCount];

        // Each MipMap has a basic header of information, and a set of compressed data blocks of info.
        for (int i = 0; i < mipCount; i++)
        {
            const int _MipMapHeaderSize = 20;
            br.BaseStream.Seek(mipMapInfoOffset + (_MipMapHeaderSize * i), SeekOrigin.Begin);

            var offsetFromHeaderEnd = br.ReadInt32();
            var mipMapLength = br.ReadInt32();
            var mipMapSize = br.ReadInt32();
            var mipMapStart = br.ReadInt32();
            var mipMapParts = br.ReadInt32();

            var mipMapPartOffset = endOfHeader + offsetFromHeaderEnd;

            br.BaseStream.Seek(mipMapPartOffset, SeekOrigin.Begin);

            var last = false;
            if (i == mipCount - 1)
            {
                last = true;
            }

            mipData[i] = BeginReadCompressedBlocks(br, mipMapParts, -1, last);
        }

        for (int i = 0; i < mipCount; i++)
        {
            var res = CompleteReadCompressedBlocks(mipData[i], decompressedData, decompOffset);
            decompOffset += res.BytesWritten;
            decompressedData = res.Buffer;
        }

        return decompressedData;
    }

    public static uint GetSqPackType(BinaryReader br, long offset = -1)
    {
        if (offset >= 0)
        {
            br.BaseStream.Seek(offset, SeekOrigin.Begin);
        }
        else
        {
            offset = br.BaseStream.Position;
        }

        br.BaseStream.Seek(offset + 4, SeekOrigin.Begin);
        var type = br.ReadUInt32();
        return type;
    }

    /// <summary>Decompresses (de-SqPacks) a given block of data.</summary>
    public static byte[] ReadSqPackFile(byte[] sqpackData)
    {
        using (var ms = new MemoryStream(sqpackData))
        {
            using (var br = new BinaryReader(ms))
            {
                return ReadSqPackFile(br);
            }
        }
    }

    /// <summary>Reads an SQPack file from the given data stream.</summary>
    public static byte[] ReadSqPackFile(BinaryReader br, long offset = -1)
    {
        if (offset >= 0)
        {
            br.BaseStream.Seek(offset, SeekOrigin.Begin);
        }
        else
        {
            offset = br.BaseStream.Position;
        }

        br.BaseStream.Seek(offset + 4, SeekOrigin.Begin);
        var type = br.ReadInt32();

        br.BaseStream.Seek(offset, SeekOrigin.Begin);
        if (type == 2)
        {
            return ReadSqPackType2(br, offset);
        }
        else if (type == 3)
        {
            return ReadSqPackType3(br, offset);
        }
        else if (type == 4)
        {
            return ReadSqPackType4(br, offset);
        }
        throw new NotImplementedException("Unable to read invalid SQPack File Type.");
    }
}
