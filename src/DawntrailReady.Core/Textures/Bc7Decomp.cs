// Port of bc7decomp.cpp (Richard Geldreich, Jr., MIT license or public domain) as built into
// JeremyAnsel.BcnSharp 1.0.6's BcnSharpLib64.dll, plus that DLL's BC7_Decode block loop, which TexTools'
// DxtUtil.DecompressBc7 calls. The x64 DLL uses the SSE2 interpolation paths; they give the same integers as the
// scalar formulas used here ((l * (64 - w) + h * w + 32) >> 6).

namespace DawntrailReady.Core.Textures;

internal static class Bc7Decomp
{
    private static readonly uint[] g_bc7_weights2 = [0, 21, 43, 64];
    private static readonly uint[] g_bc7_weights3 = [0, 9, 18, 27, 37, 46, 55, 64];
    private static readonly uint[] g_bc7_weights4 = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

    private static readonly byte[] g_bc7_partition2 =
    [
        0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1, 0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1, 0,1,1,1,0,1,1,1,0,1,1,1,0,1,1,1, 0,0,0,1,0,0,1,1,0,0,1,1,0,1,1,1, 0,0,0,0,0,0,0,1,0,0,0,1,0,0,1,1, 0,0,1,1,0,1,1,1,0,1,1,1,1,1,1,1, 0,0,0,1,0,0,1,1,0,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,1,0,0,1,1,0,1,1,1,
        0,0,0,0,0,0,0,0,0,0,0,1,0,0,1,1, 0,0,1,1,0,1,1,1,1,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,1,0,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,0,0,0,0,1,0,1,1,1, 0,0,0,1,0,1,1,1,1,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,0,1,1,1,1,1,1,1,1, 0,0,0,0,1,1,1,1,1,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,0,0,0,0,0,1,1,1,1,
        0,0,0,0,1,0,0,0,1,1,1,0,1,1,1,1, 0,1,1,1,0,0,0,1,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,1,0,0,0,1,1,1,0, 0,1,1,1,0,0,1,1,0,0,0,1,0,0,0,0, 0,0,1,1,0,0,0,1,0,0,0,0,0,0,0,0, 0,0,0,0,1,0,0,0,1,1,0,0,1,1,1,0, 0,0,0,0,0,0,0,0,1,0,0,0,1,1,0,0, 0,1,1,1,0,0,1,1,0,0,1,1,0,0,0,1,
        0,0,1,1,0,0,0,1,0,0,0,1,0,0,0,0, 0,0,0,0,1,0,0,0,1,0,0,0,1,1,0,0, 0,1,1,0,0,1,1,0,0,1,1,0,0,1,1,0, 0,0,1,1,0,1,1,0,0,1,1,0,1,1,0,0, 0,0,0,1,0,1,1,1,1,1,1,0,1,0,0,0, 0,0,0,0,1,1,1,1,1,1,1,1,0,0,0,0, 0,1,1,1,0,0,0,1,1,0,0,0,1,1,1,0, 0,0,1,1,1,0,0,1,1,0,0,1,1,1,0,0,
        0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1, 0,0,0,0,1,1,1,1,0,0,0,0,1,1,1,1, 0,1,0,1,1,0,1,0,0,1,0,1,1,0,1,0, 0,0,1,1,0,0,1,1,1,1,0,0,1,1,0,0, 0,0,1,1,1,1,0,0,0,0,1,1,1,1,0,0, 0,1,0,1,0,1,0,1,1,0,1,0,1,0,1,0, 0,1,1,0,1,0,0,1,0,1,1,0,1,0,0,1, 0,1,0,1,1,0,1,0,1,0,1,0,0,1,0,1,
        0,1,1,1,0,0,1,1,1,1,0,0,1,1,1,0, 0,0,0,1,0,0,1,1,1,1,0,0,1,0,0,0, 0,0,1,1,0,0,1,0,0,1,0,0,1,1,0,0, 0,0,1,1,1,0,1,1,1,1,0,1,1,1,0,0, 0,1,1,0,1,0,0,1,1,0,0,1,0,1,1,0, 0,0,1,1,1,1,0,0,1,1,0,0,0,0,1,1, 0,1,1,0,0,1,1,0,1,0,0,1,1,0,0,1, 0,0,0,0,0,1,1,0,0,1,1,0,0,0,0,0,
        0,1,0,0,1,1,1,0,0,1,0,0,0,0,0,0, 0,0,1,0,0,1,1,1,0,0,1,0,0,0,0,0, 0,0,0,0,0,0,1,0,0,1,1,1,0,0,1,0, 0,0,0,0,0,1,0,0,1,1,1,0,0,1,0,0, 0,1,1,0,1,1,0,0,1,0,0,1,0,0,1,1, 0,0,1,1,0,1,1,0,1,1,0,0,1,0,0,1, 0,1,1,0,0,0,1,1,1,0,0,1,1,1,0,0, 0,0,1,1,1,0,0,1,1,1,0,0,0,1,1,0,
        0,1,1,0,1,1,0,0,1,1,0,0,1,0,0,1, 0,1,1,0,0,0,1,1,0,0,1,1,1,0,0,1, 0,1,1,1,1,1,1,0,1,0,0,0,0,0,0,1, 0,0,0,1,1,0,0,0,1,1,1,0,0,1,1,1, 0,0,0,0,1,1,1,1,0,0,1,1,0,0,1,1, 0,0,1,1,0,0,1,1,1,1,1,1,0,0,0,0, 0,0,1,0,0,0,1,0,1,1,1,0,1,1,1,0, 0,1,0,0,0,1,0,0,0,1,1,1,0,1,1,1,
    ];

    private static readonly byte[] g_bc7_partition3 =
    [
        0,0,1,1,0,0,1,1,0,2,2,1,2,2,2,2, 0,0,0,1,0,0,1,1,2,2,1,1,2,2,2,1, 0,0,0,0,2,0,0,1,2,2,1,1,2,2,1,1, 0,2,2,2,0,0,2,2,0,0,1,1,0,1,1,1, 0,0,0,0,0,0,0,0,1,1,2,2,1,1,2,2, 0,0,1,1,0,0,1,1,0,0,2,2,0,0,2,2, 0,0,2,2,0,0,2,2,1,1,1,1,1,1,1,1, 0,0,1,1,0,0,1,1,2,2,1,1,2,2,1,1,
        0,0,0,0,0,0,0,0,1,1,1,1,2,2,2,2, 0,0,0,0,1,1,1,1,1,1,1,1,2,2,2,2, 0,0,0,0,1,1,1,1,2,2,2,2,2,2,2,2, 0,0,1,2,0,0,1,2,0,0,1,2,0,0,1,2, 0,1,1,2,0,1,1,2,0,1,1,2,0,1,1,2, 0,1,2,2,0,1,2,2,0,1,2,2,0,1,2,2, 0,0,1,1,0,1,1,2,1,1,2,2,1,2,2,2, 0,0,1,1,2,0,0,1,2,2,0,0,2,2,2,0,
        0,0,0,1,0,0,1,1,0,1,1,2,1,1,2,2, 0,1,1,1,0,0,1,1,2,0,0,1,2,2,0,0, 0,0,0,0,1,1,2,2,1,1,2,2,1,1,2,2, 0,0,2,2,0,0,2,2,0,0,2,2,1,1,1,1, 0,1,1,1,0,1,1,1,0,2,2,2,0,2,2,2, 0,0,0,1,0,0,0,1,2,2,2,1,2,2,2,1, 0,0,0,0,0,0,1,1,0,1,2,2,0,1,2,2, 0,0,0,0,1,1,0,0,2,2,1,0,2,2,1,0,
        0,1,2,2,0,1,2,2,0,0,1,1,0,0,0,0, 0,0,1,2,0,0,1,2,1,1,2,2,2,2,2,2, 0,1,1,0,1,2,2,1,1,2,2,1,0,1,1,0, 0,0,0,0,0,1,1,0,1,2,2,1,1,2,2,1, 0,0,2,2,1,1,0,2,1,1,0,2,0,0,2,2, 0,1,1,0,0,1,1,0,2,0,0,2,2,2,2,2, 0,0,1,1,0,1,2,2,0,1,2,2,0,0,1,1, 0,0,0,0,2,0,0,0,2,2,1,1,2,2,2,1,
        0,0,0,0,0,0,0,2,1,1,2,2,1,2,2,2, 0,2,2,2,0,0,2,2,0,0,1,2,0,0,1,1, 0,0,1,1,0,0,1,2,0,0,2,2,0,2,2,2, 0,1,2,0,0,1,2,0,0,1,2,0,0,1,2,0, 0,0,0,0,1,1,1,1,2,2,2,2,0,0,0,0, 0,1,2,0,1,2,0,1,2,0,1,2,0,1,2,0, 0,1,2,0,2,0,1,2,1,2,0,1,0,1,2,0, 0,0,1,1,2,2,0,0,1,1,2,2,0,0,1,1,
        0,0,1,1,1,1,2,2,2,2,0,0,0,0,1,1, 0,1,0,1,0,1,0,1,2,2,2,2,2,2,2,2, 0,0,0,0,0,0,0,0,2,1,2,1,2,1,2,1, 0,0,2,2,1,1,2,2,0,0,2,2,1,1,2,2, 0,0,2,2,0,0,1,1,0,0,2,2,0,0,1,1, 0,2,2,0,1,2,2,1,0,2,2,0,1,2,2,1, 0,1,0,1,2,2,2,2,2,2,2,2,0,1,0,1, 0,0,0,0,2,1,2,1,2,1,2,1,2,1,2,1,
        0,1,0,1,0,1,0,1,0,1,0,1,2,2,2,2, 0,2,2,2,0,1,1,1,0,2,2,2,0,1,1,1, 0,0,0,2,1,1,1,2,0,0,0,2,1,1,1,2, 0,0,0,0,2,1,1,2,2,1,1,2,2,1,1,2, 0,2,2,2,0,1,1,1,0,1,1,1,0,2,2,2, 0,0,0,2,1,1,1,2,1,1,1,2,0,0,0,2, 0,1,1,0,0,1,1,0,0,1,1,0,2,2,2,2, 0,0,0,0,0,0,0,0,2,1,1,2,2,1,1,2,
        0,1,1,0,0,1,1,0,2,2,2,2,2,2,2,2, 0,0,2,2,0,0,1,1,0,0,1,1,0,0,2,2, 0,0,2,2,1,1,2,2,1,1,2,2,0,0,2,2, 0,0,0,0,0,0,0,0,0,0,0,0,2,1,1,2, 0,0,0,2,0,0,0,1,0,0,0,2,0,0,0,1, 0,2,2,2,1,2,2,2,0,2,2,2,1,2,2,2, 0,1,0,1,2,2,2,2,2,2,2,2,2,2,2,2, 0,1,1,1,2,0,1,1,2,2,0,1,2,2,2,0,
    ];

    private static readonly byte[] g_bc7_table_anchor_index_second_subset =
    [
        15,15,15,15,15,15,15,15, 15,15,15,15,15,15,15,15, 15, 2, 8, 2, 2, 8, 8,15, 2, 8, 2, 2, 8, 8, 2, 2,
        15,15, 6, 8, 2, 8,15,15, 2, 8, 2, 2, 2,15,15, 6, 6, 2, 6, 8,15,15, 2, 2, 15,15,15,15,15, 2, 2,15,
    ];

    private static readonly byte[] g_bc7_table_anchor_index_third_subset_1 =
    [
        3, 3,15,15, 8, 3,15,15, 8, 8, 6, 6, 6, 5, 3, 3, 3, 3, 8,15, 3, 3, 6,10, 5, 8, 8, 6, 8, 5,15,15,
        8,15, 3, 5, 6,10, 8,15, 15, 3,15, 5,15,15,15,15, 3,15, 5, 5, 5, 8, 5,10, 5,10, 8,13,15,12, 3, 3,
    ];

    private static readonly byte[] g_bc7_table_anchor_index_third_subset_2 =
    [
        15, 8, 8, 3,15,15, 3, 8, 15,15,15,15,15,15,15, 8, 15, 8,15, 3,15, 8,15, 8, 3,15, 6,10,15,15,10, 8,
        15, 3,15,10,10, 8, 9,10, 6,15, 8,15, 3, 6, 6, 8, 15, 3,15,15,15,15,15,15, 15,15,15,15, 3,15,15, 8,
    ];

    // bc7decomp's g_bc7_first_byte_to_mode: the index of the lowest set bit, 8 when the byte is 0.
    private static uint FirstByteToMode(byte b)
    {
        return b == 0 ? 8u : (uint)System.Numerics.BitOperations.TrailingZeroCount(b);
    }

    // BcnSharpLib BC7_Decode: blocks in order, each pixel stored as B,G,R,A.
    internal static void Bc7Decode(byte[] pBlock, byte[] pPixelsRGBA, int width, int height)
    {
        var block = new byte[4 * 4 * 4];
        var blockOffset = 0;

        for (int y = 0; y < height; y += 4)
        {
            for (int x = 0; x < width; x += 4)
            {
                int maxJ = Math.Min(height - y, 4);
                int maxI = Math.Min(width - x, 4);

                UnpackBc7(pBlock, blockOffset, block);

                for (int j = 0; j < maxJ; j++)
                {
                    for (int i = 0; i < maxI; i++)
                    {
                        int sourceOffset = (y + j) * width * 4 + (x + i) * 4;
                        int destinationOffset = j * 4 * 4 + i * 4;

                        pPixelsRGBA[sourceOffset + 0] = block[destinationOffset + 2];
                        pPixelsRGBA[sourceOffset + 1] = block[destinationOffset + 1];
                        pPixelsRGBA[sourceOffset + 2] = block[destinationOffset + 0];
                        pPixelsRGBA[sourceOffset + 3] = block[destinationOffset + 3];
                    }
                }

                blockOffset += 16;
            }
        }
    }

    private static void insert_weight_zero(ref ulong index_bits, uint bits_per_index, uint offset)
    {
        ulong LOW_BIT_MASK = (1UL << (int)((bits_per_index * (offset + 1)) - 1)) - 1;
        ulong HIGH_BIT_MASK = ~LOW_BIT_MASK;

        index_bits = ((index_bits & HIGH_BIT_MASK) << 1) | (index_bits & LOW_BIT_MASK);
    }

    private static uint bc7_dequant(uint val, uint pbit, uint val_bits)
    {
        uint total_bits = val_bits + 1;
        val = (val << 1) | pbit;
        val <<= (int)(8 - total_bits);
        val |= (val >> (int)total_bits);
        return val;
    }

    private static uint bc7_dequant(uint val, uint val_bits)
    {
        val <<= (int)(8 - val_bits);
        val |= (val >> (int)val_bits);
        return val;
    }

    private static uint bc7_interp(uint l, uint h, uint w, uint bits)
    {
        switch (bits)
        {
            case 2: return (l * (64 - g_bc7_weights2[w]) + h * g_bc7_weights2[w] + 32) >> 6;
            case 3: return (l * (64 - g_bc7_weights3[w]) + h * g_bc7_weights3[w] + 32) >> 6;
            case 4: return (l * (64 - g_bc7_weights4[w]) + h * g_bc7_weights4[w] + 32) >> 6;
        }
        return 0;
    }

    // pPixels receives 16 RGBA pixels, row major.
    internal static bool UnpackBc7(byte[] src, int srcOffset, byte[] pPixels)
    {
        uint mode = FirstByteToMode(src[srcOffset]);

        var data_chunks = new ulong[2];
        data_chunks[0] = BitConverter.ToUInt64(src, srcOffset);
        data_chunks[1] = BitConverter.ToUInt64(src, srcOffset + 8);

        switch (mode)
        {
            case 0:
            case 2:
                return unpack_bc7_mode0_2(mode, data_chunks, pPixels);
            case 1:
            case 3:
            case 7:
                return unpack_bc7_mode1_3_7(mode, data_chunks, pPixels);
            case 4:
            case 5:
                return unpack_bc7_mode4_5(mode, data_chunks, pPixels);
            case 6:
                return unpack_bc7_mode6(data_chunks, pPixels);
            default:
                Array.Clear(pPixels, 0, 64);
                break;
        }

        return false;
    }

    private static bool unpack_bc7_mode0_2(uint mode, ulong[] data_chunks, byte[] pPixels)
    {
        const uint ENDPOINTS = 6;
        const uint COMPS = 3;
        uint WEIGHT_BITS = (mode == 0) ? 3u : 2u;
        uint WEIGHT_MASK = (1u << (int)WEIGHT_BITS) - 1;
        uint ENDPOINT_BITS = (mode == 0) ? 4u : 5u;
        uint ENDPOINT_MASK = (1u << (int)ENDPOINT_BITS) - 1;
        uint PBITS = (mode == 0) ? 6u : 0u;
        uint WEIGHT_VALS = 1u << (int)WEIGHT_BITS;
        uint PART_BITS = (mode == 0) ? 4u : 6u;
        uint PART_MASK = (1u << (int)PART_BITS) - 1;

        ulong low_chunk = data_chunks[0];
        ulong high_chunk = data_chunks[1];

        uint part = (uint)((low_chunk >> (int)(mode + 1)) & PART_MASK);

        var channel_read_chunks = new ulong[3];

        if (mode == 0)
        {
            channel_read_chunks[0] = low_chunk >> 5;
            channel_read_chunks[1] = low_chunk >> 29;
            channel_read_chunks[2] = ((low_chunk >> 53) | (high_chunk << 11));
        }
        else
        {
            channel_read_chunks[0] = low_chunk >> 9;
            channel_read_chunks[1] = ((low_chunk >> 39) | (high_chunk << 25));
            channel_read_chunks[2] = high_chunk >> 5;
        }

        var endpoints = new byte[ENDPOINTS * 4];
        for (uint c = 0; c < COMPS; c++)
        {
            ulong channel_read_chunk = channel_read_chunks[c];
            for (uint e = 0; e < ENDPOINTS; e++)
            {
                endpoints[e * 4 + c] = (byte)(channel_read_chunk & ENDPOINT_MASK);
                channel_read_chunk >>= (int)ENDPOINT_BITS;
            }
        }

        var pbits = new uint[6];
        if (mode == 0)
        {
            byte p_bits_chunk = (byte)((high_chunk >> 13) & 0xff);

            for (uint p = 0; p < PBITS; p++)
                pbits[p] = (uint)(p_bits_chunk >> (int)p) & 1;
        }

        ulong weights_read_chunk = high_chunk >> (int)(67 - 16 * WEIGHT_BITS);
        insert_weight_zero(ref weights_read_chunk, WEIGHT_BITS, 0);
        insert_weight_zero(ref weights_read_chunk, WEIGHT_BITS, Math.Min(g_bc7_table_anchor_index_third_subset_1[part], g_bc7_table_anchor_index_third_subset_2[part]));
        insert_weight_zero(ref weights_read_chunk, WEIGHT_BITS, Math.Max(g_bc7_table_anchor_index_third_subset_1[part], g_bc7_table_anchor_index_third_subset_2[part]));

        var weights = new uint[16];
        for (uint i = 0; i < 16; i++)
        {
            weights[i] = (uint)(weights_read_chunk & WEIGHT_MASK);
            weights_read_chunk >>= (int)WEIGHT_BITS;
        }

        for (uint e = 0; e < ENDPOINTS; e++)
            for (uint c = 0; c < 4; c++)
                endpoints[e * 4 + c] = (byte)((c == 3) ? 255 : (PBITS != 0 ? bc7_dequant(endpoints[e * 4 + c], pbits[e], ENDPOINT_BITS) : bc7_dequant(endpoints[e * 4 + c], ENDPOINT_BITS)));

        var block_colors = new byte[3 * 8 * 4];
        for (uint s = 0; s < 3; s++)
            for (uint i = 0; i < WEIGHT_VALS; i++)
            {
                for (uint c = 0; c < 3; c++)
                    block_colors[(s * 8 + i) * 4 + c] = (byte)bc7_interp(endpoints[(s * 2 + 0) * 4 + c], endpoints[(s * 2 + 1) * 4 + c], i, WEIGHT_BITS);
                block_colors[(s * 8 + i) * 4 + 3] = 255;
            }

        for (uint i = 0; i < 16; i++)
            Array.Copy(block_colors, (g_bc7_partition3[part * 16 + i] * 8 + weights[i]) * 4, pPixels, i * 4, 4);

        return true;
    }

    private static bool unpack_bc7_mode1_3_7(uint mode, ulong[] data_chunks, byte[] pPixels)
    {
        const uint ENDPOINTS = 4;
        uint COMPS = (mode == 7) ? 4u : 3u;
        uint WEIGHT_BITS = (mode == 1) ? 3u : 2u;
        uint WEIGHT_MASK = (1u << (int)WEIGHT_BITS) - 1;
        uint ENDPOINT_BITS = (mode == 7) ? 5u : ((mode == 1) ? 6u : 7u);
        uint ENDPOINT_MASK = (1u << (int)ENDPOINT_BITS) - 1;
        uint PBITS = (mode == 1) ? 2u : 4u;
        bool SHARED_PBITS = mode == 1;
        uint WEIGHT_VALS = 1u << (int)WEIGHT_BITS;

        ulong low_chunk = data_chunks[0];
        ulong high_chunk = data_chunks[1];

        uint part = (uint)((low_chunk >> (int)(mode + 1)) & 0x3f);

        var endpoints = new byte[ENDPOINTS * 4];

        var channel_read_chunks = new ulong[4];
        ulong p_read_chunk;
        channel_read_chunks[0] = (low_chunk >> (int)(mode + 7));
        ulong weight_read_chunk;

        switch (mode)
        {
            case 1:
                channel_read_chunks[1] = (low_chunk >> 32);
                channel_read_chunks[2] = ((low_chunk >> 56) | (high_chunk << 8));
                p_read_chunk = high_chunk >> 16;
                weight_read_chunk = high_chunk >> 18;
                break;
            case 3:
                channel_read_chunks[1] = ((low_chunk >> 38) | (high_chunk << 26));
                channel_read_chunks[2] = high_chunk >> 2;
                p_read_chunk = high_chunk >> 30;
                weight_read_chunk = high_chunk >> 34;
                break;
            case 7:
                channel_read_chunks[1] = low_chunk >> 34;
                channel_read_chunks[2] = ((low_chunk >> 54) | (high_chunk << 10));
                channel_read_chunks[3] = high_chunk >> 10;
                p_read_chunk = (high_chunk >> 30);
                weight_read_chunk = (high_chunk >> 34);
                break;
            default:
                return false;
        }

        for (uint c = 0; c < COMPS; c++)
        {
            ulong channel_read_chunk = channel_read_chunks[c];
            for (uint e = 0; e < ENDPOINTS; e++)
            {
                endpoints[e * 4 + c] = (byte)(channel_read_chunk & ENDPOINT_MASK);
                channel_read_chunk >>= (int)ENDPOINT_BITS;
            }
        }

        var pbits = new uint[4];
        for (uint p = 0; p < PBITS; p++)
            pbits[p] = (uint)(p_read_chunk >> (int)p) & 1;

        insert_weight_zero(ref weight_read_chunk, WEIGHT_BITS, 0);
        insert_weight_zero(ref weight_read_chunk, WEIGHT_BITS, g_bc7_table_anchor_index_second_subset[part]);

        var weights = new uint[16];
        for (uint i = 0; i < 16; i++)
        {
            weights[i] = (uint)(weight_read_chunk & WEIGHT_MASK);
            weight_read_chunk >>= (int)WEIGHT_BITS;
        }

        for (uint e = 0; e < ENDPOINTS; e++)
            for (uint c = 0; c < 4; c++)
                endpoints[e * 4 + c] = (byte)((mode != 7u && c == 3u) ? 255 : bc7_dequant(endpoints[e * 4 + c], pbits[SHARED_PBITS ? (e >> 1) : e], ENDPOINT_BITS));

        var block_colors = new byte[2 * 8 * 4];
        for (uint s = 0; s < 2; s++)
            for (uint i = 0; i < WEIGHT_VALS; i++)
            {
                // With 3 components the alpha endpoints are both 255, so interpolating gives 255 as well.
                for (uint c = 0; c < 4; c++)
                    block_colors[(s * 8 + i) * 4 + c] = (byte)bc7_interp(endpoints[(s * 2 + 0) * 4 + c], endpoints[(s * 2 + 1) * 4 + c], i, WEIGHT_BITS);
            }

        for (uint i = 0; i < 16; i++)
            Array.Copy(block_colors, (g_bc7_partition2[part * 16 + i] * 8 + weights[i]) * 4, pPixels, i * 4, 4);

        return true;
    }

    private static bool unpack_bc7_mode4_5(uint mode, ulong[] data_chunks, byte[] pPixels)
    {
        const uint ENDPOINTS = 2;
        const uint WEIGHT_BITS = 2;
        const uint WEIGHT_MASK = (1 << (int)WEIGHT_BITS) - 1;
        uint A_WEIGHT_BITS = (mode == 4) ? 3u : 2u;
        uint A_WEIGHT_MASK = (1u << (int)A_WEIGHT_BITS) - 1;
        uint ENDPOINT_BITS = (mode == 4) ? 5u : 7u;
        uint ENDPOINT_MASK = (1u << (int)ENDPOINT_BITS) - 1;
        uint A_ENDPOINT_BITS = (mode == 4) ? 6u : 8u;
        uint A_ENDPOINT_MASK = (1u << (int)A_ENDPOINT_BITS) - 1;

        ulong low_chunk = data_chunks[0];
        ulong high_chunk = data_chunks[1];

        uint comp_rot = (uint)((low_chunk >> (int)(mode + 1)) & 0x3);
        uint index_mode = (mode == 4) ? (uint)((low_chunk >> 7) & 1) : 0;

        ulong color_read_bits = low_chunk >> 8;

        var endpoints = new byte[ENDPOINTS * 4];
        for (uint c = 0; c < 3; c++)
        {
            for (uint e = 0; e < ENDPOINTS; e++)
            {
                endpoints[e * 4 + c] = (byte)(color_read_bits & ENDPOINT_MASK);
                color_read_bits >>= (int)ENDPOINT_BITS;
            }
        }

        endpoints[3] = (byte)(color_read_bits & ENDPOINT_MASK);

        ulong rgb_weights_chunk;
        ulong a_weights_chunk;
        if (mode == 4)
        {
            endpoints[3] = (byte)(color_read_bits & A_ENDPOINT_MASK);
            endpoints[4 + 3] = (byte)((color_read_bits >> (int)A_ENDPOINT_BITS) & A_ENDPOINT_MASK);
            rgb_weights_chunk = ((low_chunk >> 50) | (high_chunk << 14));
            a_weights_chunk = high_chunk >> 17;
        }
        else if (mode == 5)
        {
            endpoints[3] = (byte)(color_read_bits & A_ENDPOINT_MASK);
            endpoints[4 + 3] = (byte)(((low_chunk >> 58) | (high_chunk << 6)) & A_ENDPOINT_MASK);
            rgb_weights_chunk = high_chunk >> 2;
            a_weights_chunk = high_chunk >> 33;
        }
        else
            return false;

        insert_weight_zero(ref rgb_weights_chunk, WEIGHT_BITS, 0);
        insert_weight_zero(ref a_weights_chunk, A_WEIGHT_BITS, 0);

        uint[] weight_bits = [index_mode != 0 ? A_WEIGHT_BITS : WEIGHT_BITS, index_mode != 0 ? WEIGHT_BITS : A_WEIGHT_BITS];
        uint[] weight_mask = [index_mode != 0 ? A_WEIGHT_MASK : WEIGHT_MASK, index_mode != 0 ? WEIGHT_MASK : A_WEIGHT_MASK];

        var weights = new uint[16];
        var a_weights = new uint[16];

        if (index_mode != 0)
            (rgb_weights_chunk, a_weights_chunk) = (a_weights_chunk, rgb_weights_chunk);

        for (uint i = 0; i < 16; i++)
        {
            weights[i] = (uint)(rgb_weights_chunk & weight_mask[0]);
            rgb_weights_chunk >>= (int)weight_bits[0];
        }

        for (uint i = 0; i < 16; i++)
        {
            a_weights[i] = (uint)(a_weights_chunk & weight_mask[1]);
            a_weights_chunk >>= (int)weight_bits[1];
        }

        for (uint e = 0; e < ENDPOINTS; e++)
            for (uint c = 0; c < 4; c++)
                endpoints[e * 4 + c] = (byte)bc7_dequant(endpoints[e * 4 + c], (c == 3) ? A_ENDPOINT_BITS : ENDPOINT_BITS);

        var block_colors = new byte[8 * 4];
        for (uint i = 0; i < (1u << (int)weight_bits[0]); i++)
            for (uint c = 0; c < 3; c++)
                block_colors[i * 4 + c] = (byte)bc7_interp(endpoints[c], endpoints[4 + c], i, weight_bits[0]);

        for (uint i = 0; i < (1u << (int)weight_bits[1]); i++)
            block_colors[i * 4 + 3] = (byte)bc7_interp(endpoints[3], endpoints[4 + 3], i, weight_bits[1]);

        for (uint i = 0; i < 16; i++)
        {
            var p = i * 4;
            pPixels[p + 0] = block_colors[weights[i] * 4 + 0];
            pPixels[p + 1] = block_colors[weights[i] * 4 + 1];
            pPixels[p + 2] = block_colors[weights[i] * 4 + 2];
            pPixels[p + 3] = block_colors[a_weights[i] * 4 + 3];
            if (comp_rot >= 1)
                (pPixels[p + 3], pPixels[p + comp_rot - 1]) = (pPixels[p + comp_rot - 1], pPixels[p + 3]);
        }

        return true;
    }

    private static bool unpack_bc7_mode6(ulong[] data_chunks, byte[] pPixels)
    {
        ulong lo = data_chunks[0];
        ulong hi = data_chunks[1];

        if ((lo & 0x7F) != (1 << 6))
            return false;

        uint p0 = (uint)(lo >> 63) & 1;
        uint p1 = (uint)hi & 1;
        uint Field(int shift) => (uint)(lo >> shift) & 0x7F;

        uint r0 = (Field(7) << 1) | p0;
        uint g0 = (Field(21) << 1) | p0;
        uint b0 = (Field(35) << 1) | p0;
        uint a0 = (Field(49) << 1) | p0;
        uint r1 = (Field(14) << 1) | p1;
        uint g1 = (Field(28) << 1) | p1;
        uint b1 = (Field(42) << 1) | p1;
        uint a1 = (Field(56) << 1) | p1;

        var vals = new byte[16 * 4];
        for (uint i = 0; i < 16; i++)
        {
            uint w = g_bc7_weights4[i];
            uint iw = 64 - w;
            vals[i * 4 + 0] = (byte)((r0 * iw + r1 * w + 32) >> 6);
            vals[i * 4 + 1] = (byte)((g0 * iw + g1 * w + 32) >> 6);
            vals[i * 4 + 2] = (byte)((b0 * iw + b1 * w + 32) >> 6);
            vals[i * 4 + 3] = (byte)((a0 * iw + a1 * w + 32) >> 6);
        }

        // Selector 0 is 3 bits wide (bits 1..3 of the high half), the other 15 are 4 bits each.
        for (int i = 0; i < 16; i++)
        {
            uint sel = i == 0 ? (uint)(hi >> 1) & 0x7 : (uint)(hi >> (4 + (i - 1) * 4)) & 0xF;
            Array.Copy(vals, sel * 4, pPixels, i * 4, 4);
        }

        return true;
    }
}
