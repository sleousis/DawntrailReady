// Port of xivModdingFramework (TexTools, GPL-3.0): Materials/FileTypes/Mtrl.cs
//   GetXivMtrl(byte[], string) and XivMtrlToUncompressedMtrl(XivMtrl),
// plus the helpers they call: Helpers/IOUtil.cs (ReadNullTerminatedString, ReplaceBytesAt) and
// SqPack/FileTypes/Dat.cs (Pad<T>(List<T>, int, bool)).
using System.Text;

namespace DawntrailReady.Core.Materials;

/// <summary>
/// This class contains the methods that deal with the .mtrl file type
/// </summary>
public static class Mtrl
{
    // NOT PORTED: the transaction/item based GetXivMtrl overloads, GetTexturePathsFromMtrlPath, colorset image
    // export, ImportMtrl*, CreateDefaultMaterial, CompareMaterials, path helpers and the shader DB builders.
    // They need TexTools' transactions, item database or SQLite, and the upgrade only needs bytes in / bytes out.

    public const string EmptySamplerPrefix = "_EMPTY_SAMPLER_";

    /// <summary>
    /// Converts an uncompressed .MTRL file into an XivMtrl.
    /// Path is only used to staple it onto the resulting XivMtrl's MTRLPath value.
    /// </summary>
    public static XivMtrl GetXivMtrl(byte[] bytes, string internalMtrlPath = "")
    {
        var xivMtrl = new XivMtrl();
        using (var br = new BinaryReader(new MemoryStream(bytes)))
        {
            xivMtrl = new XivMtrl
            {
                MTRLPath = internalMtrlPath,
                Signature = br.ReadInt32(),
            };
            var fileSize = br.ReadInt16();

            var colorSetDataSize = br.ReadUInt16();
            var stringBlockSize = br.ReadUInt16();
            var shaderNameOffset = br.ReadUInt16();
            var texCount = br.ReadByte();
            var mapCount = br.ReadByte();
            var colorsetCount = br.ReadByte();
            var additionalDataSize = br.ReadByte();

            xivMtrl.Textures = new List<MtrlTexture>();

            // Texture String Information.
            var texPathOffsets = new List<int>(texCount);
            var texFlags = new List<short>(texCount);
            for (var i = 0; i < texCount; i++)
            {
                var tex = new MtrlTexture();
                texPathOffsets.Add(br.ReadInt16());

                var flags = br.ReadInt16();
                texFlags.Add(flags);
                tex.Flags = (ushort)flags;
                xivMtrl.Textures.Add(tex);
            }

            // Map String Information.
            var mapOffset = new List<int>(mapCount);
            xivMtrl.UvMapStrings = new List<MtrlString>();
            for (var i = 0; i < mapCount; i++)
            {
                mapOffset.Add(br.ReadInt16());

                var map = new MtrlString();
                map.Flags = br.ReadUInt16();
                xivMtrl.UvMapStrings.Add(map);
            }

            // Colorset String Information.
            var colorsetOffsets = new List<int>(colorsetCount);
            xivMtrl.ColorsetStrings = new List<MtrlString>();
            for (var i = 0; i < colorsetCount; i++)
            {
                colorsetOffsets.Add(br.ReadInt16());

                var colorset = new MtrlString();
                colorset.Flags = br.ReadUInt16();
                xivMtrl.ColorsetStrings.Add(colorset);
            }

            var stringBlockStart = br.BaseStream.Position;
            for (var i = 0; i < texCount; i++)
            {
                br.BaseStream.Seek(stringBlockStart + texPathOffsets[i], SeekOrigin.Begin);
                var path = ReadNullTerminatedString(br);
                xivMtrl.Textures[i].TexturePath = path;
            }

            for (var i = 0; i < xivMtrl.UvMapStrings.Count; i++)
            {
                br.BaseStream.Seek(stringBlockStart + mapOffset[i], SeekOrigin.Begin);
                var st = ReadNullTerminatedString(br);
                xivMtrl.UvMapStrings[i].Value = st;
            }

            for (var i = 0; i < xivMtrl.ColorsetStrings.Count; i++)
            {
                br.BaseStream.Seek(stringBlockStart + colorsetOffsets[i], SeekOrigin.Begin);
                var st = ReadNullTerminatedString(br);
                xivMtrl.ColorsetStrings[i].Value = st;
            }

            br.BaseStream.Seek(stringBlockStart + shaderNameOffset, SeekOrigin.Begin);
            xivMtrl.ShaderPackRaw = ReadNullTerminatedString(br);

            br.BaseStream.Seek(stringBlockStart + stringBlockSize, SeekOrigin.Begin);

            xivMtrl.AdditionalData = br.ReadBytes(additionalDataSize);

            xivMtrl.ColorSetData = new List<Half>();
            xivMtrl.ColorSetDyeData = new byte[0];

            if (colorSetDataSize > 0)
            {
                // Color Data is always 512 (6 x 14 = 64 x 8bpp = 512)
                // DT: Color Data is always 2048 instead
                var colorDataSize = (colorSetDataSize >= 2048) ? 2048 : 512;

                for (var i = 0; i < colorDataSize / 2; i++)
                {
                    // TexTools: new SharpDX.Half(ushort) - stores the raw bits.
                    xivMtrl.ColorSetData.Add(BitConverter.UInt16BitsToHalf(br.ReadUInt16()));
                }

                // If the color set is 544 (DT: 2080) in length, it has an extra 32 bytes at the end
                if (colorSetDataSize == colorDataSize + 32)
                {
                    // Endwalker style Dye Data (2 bytes per row): 5 bits flags, 11 bits template ID.
                    xivMtrl.ColorSetDyeData = br.ReadBytes(32);
                }
                if (colorSetDataSize == colorDataSize + 128)
                {
                    // Dawntrail style Dye Data (4 bytes per row): 12 bits flags, 4 unknown, 11 template ID,
                    // 2 dye channel selector, 3 unknown.
                    xivMtrl.ColorSetDyeData = br.ReadBytes(128);
                }
            }

            var shaderConstantsDataSize = br.ReadUInt16();

            var shaderKeysCount = br.ReadUInt16();
            var shaderConstantsCount = br.ReadUInt16();
            var textureSamplerCount = br.ReadUInt16();

            xivMtrl.MaterialFlags = (EMaterialFlags1)br.ReadUInt16();
            xivMtrl.MaterialFlags2 = (EMaterialFlags2)br.ReadUInt16();

            xivMtrl.ShaderKeys = new List<ShaderKey>((int)shaderKeysCount);
            for (var i = 0; i < shaderKeysCount; i++)
            {
                xivMtrl.ShaderKeys.Add(new ShaderKey
                {
                    KeyId = br.ReadUInt32(),
                    Value = br.ReadUInt32()
                });
            }

            xivMtrl.ShaderConstants = new List<ShaderConstant>(shaderConstantsCount);
            var constantOffsets = new List<short>();
            var constantSizes = new List<short>();
            for (var i = 0; i < shaderConstantsCount; i++)
            {
                xivMtrl.ShaderConstants.Add(new ShaderConstant
                {
                    ConstantId = br.ReadUInt32()
                });
                constantOffsets.Add(br.ReadInt16());
                constantSizes.Add(br.ReadInt16());
            }

            for (var i = 0; i < textureSamplerCount; i++)
            {
                var sampler = new TextureSampler
                {
                    SamplerIdRaw = br.ReadUInt32(),
                    SamplerSettingsRaw = br.ReadUInt32(),
                };

                var textureIndex = br.ReadByte();
                var padding = br.ReadBytes(3);

                if (xivMtrl.Textures.Count > textureIndex)
                {
                    if (xivMtrl.Textures[textureIndex] != null
                        && xivMtrl.Textures[textureIndex].Sampler != null
                        && xivMtrl.Textures[textureIndex].Sampler.SamplerId != ESamplerId.Unknown)
                    {
                        // We already added a sampler.

                        if (sampler.SamplerId == ESamplerId.g_SamplerColorMap0
                        || sampler.SamplerId == ESamplerId.g_SamplerSpecularMap0
                        || sampler.SamplerId == ESamplerId.g_SamplerNormalMap0)
                        {
                            // Keep new sampler.
                            xivMtrl.Textures[textureIndex].Sampler = sampler;
                        }
                        else
                        {
                            // Keep old sampler.
                            continue;
                        }
                    }
                    else
                    {
                        xivMtrl.Textures[textureIndex].Sampler = sampler;
                    }
                }
                else
                {
                    // Create a fake texture to hold this sampler.
                    var tex = new MtrlTexture();
                    tex.TexturePath = EmptySamplerPrefix + sampler.SamplerId;
                    tex.Sampler = sampler;
                    xivMtrl.Textures.Add(tex);
                }
            }

            // As TexTools: constants are read back to back; their stored offsets are only used for the count.
            var bytesRead = 0;
            for (int i = 0; i < xivMtrl.ShaderConstants.Count; i++)
            {
                var shaderConstant = xivMtrl.ShaderConstants[i];
                var offset = constantOffsets[i];
                var size = constantSizes[i];
                shaderConstant.Values = new List<float>();
                if (bytesRead + size <= shaderConstantsDataSize)
                {
                    for (var idx = offset; idx < offset + size; idx += 4)
                    {
                        var arg = br.ReadSingle();
                        shaderConstant.Values.Add(arg);
                        bytesRead += 4;
                    }
                }
                else
                {
                    // Just use a blank array if we have missing/invalid shader data.
                    shaderConstant.Values = new List<float>(new float[size / 4]);
                }
            }

            // Chew through any remaining padding.
            while (bytesRead < shaderConstantsDataSize)
            {
                br.ReadByte();
                bytesRead++;
            }
        }

        return xivMtrl;
    }

    /// <summary>
    /// Converts an XivMtrl object into the raw bytes of an uncompressed MTRL file.
    /// As TexTools, this lower-cases the texture paths of <paramref name="xivMtrl"/> and sets/clears bit 0x08 of
    /// AdditionalData[0] in place.
    /// </summary>
    public static byte[] XivMtrlToUncompressedMtrl(XivMtrl xivMtrl)
    {
        foreach (var tex in xivMtrl.Textures)
        {
            tex.TexturePath = tex.TexturePath.ToLower();
        }

        var mtrlBytes = new List<byte>();

        mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.Signature));

        var fileSizePointer = mtrlBytes.Count;
        mtrlBytes.AddRange(BitConverter.GetBytes((ushort)0)); // File Size - Backfilled later
        mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ColorSetDataSize));

        var materialDataSizePointer = mtrlBytes.Count;
        mtrlBytes.AddRange(BitConverter.GetBytes((ushort)0)); // String Block Size - Backfilled later

        var shaderNamePointerPointer = mtrlBytes.Count;
        mtrlBytes.AddRange(BitConverter.GetBytes((ushort)0)); // Shader Name Offset - Backfilled later

        mtrlBytes.Add((byte)xivMtrl.Textures.Count(x => !x.TexturePath.StartsWith(EmptySamplerPrefix)));
        mtrlBytes.Add((byte)xivMtrl.UvMapStrings.Count);
        mtrlBytes.Add((byte)xivMtrl.ColorsetStrings.Count);
        mtrlBytes.Add((byte)xivMtrl.AdditionalData.Length);

        // Build the string block and save the various offsets.
        var stringBlock = new List<byte>();

        var textureOffsets = new List<int>();
        var mapOffsets = new List<int>();
        var colorsetOffsets = new List<int>();

        foreach (var tex in xivMtrl.Textures)
        {
            // Ignore placeholder textures for empty samplers.
            if (tex.TexturePath.StartsWith(EmptySamplerPrefix)) continue;

            textureOffsets.Add(stringBlock.Count);
            var path = tex.TexturePath;

            stringBlock.AddRange(Encoding.UTF8.GetBytes(path));
            stringBlock.Add(0);
        }

        foreach (var mapPathString in xivMtrl.UvMapStrings)
        {
            mapOffsets.Add(stringBlock.Count);
            stringBlock.AddRange(Encoding.UTF8.GetBytes(mapPathString.Value));
            stringBlock.Add(0);
        }

        foreach (var colorSetPathString in xivMtrl.ColorsetStrings)
        {
            colorsetOffsets.Add(stringBlock.Count);
            stringBlock.AddRange(Encoding.UTF8.GetBytes(colorSetPathString.Value));
            stringBlock.Add(0);
        }

        var shaderNamePointer = (ushort)stringBlock.Count;
        stringBlock.AddRange(Encoding.UTF8.GetBytes(xivMtrl.ShaderPackRaw));
        stringBlock.Add(0);

        Pad(stringBlock, 4);

        // Write the new offset list.
        for (var i = 0; i < xivMtrl.Textures.Count; i++)
        {
            // Ignore placeholder textures for empty samplers.
            if (xivMtrl.Textures[i].TexturePath.StartsWith(EmptySamplerPrefix)) continue;

            // As TexTools: indexed by texture index, which assumes placeholder textures come last (they do after
            // GetXivMtrl, which appends them).
            mtrlBytes.AddRange(BitConverter.GetBytes((short)textureOffsets[i]));
            mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.Textures[i].Flags));
        }

        for (var i = 0; i < mapOffsets.Count; i++)
        {
            mtrlBytes.AddRange(BitConverter.GetBytes((short)mapOffsets[i]));
            mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.UvMapStrings[i].Flags));
        }

        for (var i = 0; i < colorsetOffsets.Count; i++)
        {
            mtrlBytes.AddRange(BitConverter.GetBytes((short)colorsetOffsets[i]));
            mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.ColorsetStrings[i].Flags));
        }

        // Add the actual string block.
        mtrlBytes.AddRange(stringBlock);

        // Set additional data flags as needed for colorset/dye information.
        // (As TexTools: throws IndexOutOfRangeException when AdditionalData is empty.)
        if (xivMtrl.ColorSetDyeData != null && xivMtrl.ColorSetDyeData.Length > 0)
        {
            xivMtrl.AdditionalData[0] |= 0x08;
        }
        else
        {
            unchecked
            {
                xivMtrl.AdditionalData[0] &= (byte)(~0x08);
            }
        }
        // TexTools has a commented-out equivalent for bit 0x04 (colorset present); it is intentionally not applied.

        mtrlBytes.AddRange(xivMtrl.AdditionalData);

        // Colorset and Dye info.
        foreach (var colorSetHalf in xivMtrl.ColorSetData)
        {
            mtrlBytes.AddRange(BitConverter.GetBytes(BitConverter.HalfToUInt16Bits(colorSetHalf)));
        }

        if (xivMtrl.ColorSetDyeData != null && xivMtrl.ColorSetDyeData.Length > 0)
        {
            mtrlBytes.AddRange(xivMtrl.ColorSetDyeData);
        }

        mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ShaderConstantsDataSize));
        mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ShaderKeyCount));
        mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ShaderConstantsCount));
        mtrlBytes.AddRange(BitConverter.GetBytes((ushort)xivMtrl.GetRealSamplerCount()));

        mtrlBytes.AddRange(BitConverter.GetBytes((ushort)xivMtrl.MaterialFlags));
        mtrlBytes.AddRange(BitConverter.GetBytes((ushort)xivMtrl.MaterialFlags2));

        foreach (var dataStruct1 in xivMtrl.ShaderKeys)
        {
            mtrlBytes.AddRange(BitConverter.GetBytes(dataStruct1.KeyId));
            mtrlBytes.AddRange(BitConverter.GetBytes(dataStruct1.Value));
        }

        var offset = 0;
        foreach (var parameter in xivMtrl.ShaderConstants)
        {
            // Ensure we're writing correctly calculated data.
            short byteSize = (short)(parameter.Values.Count * 4);

            mtrlBytes.AddRange(BitConverter.GetBytes((uint)parameter.ConstantId));
            mtrlBytes.AddRange(BitConverter.GetBytes((ushort)offset));
            mtrlBytes.AddRange(BitConverter.GetBytes((ushort)byteSize));
            offset += parameter.Values.Count * 4;
        }

        for (int i = 0; i < xivMtrl.Textures.Count; i++)
        {
            var tex = xivMtrl.Textures[i];
            if (tex.Sampler != null)
            {
                if (tex.TexturePath.StartsWith(EmptySamplerPrefix))
                {
                    mtrlBytes.AddRange(BitConverter.GetBytes(tex.Sampler.SamplerIdRaw));
                    mtrlBytes.AddRange(BitConverter.GetBytes(tex.Sampler.SamplerSettingsRaw));

                    // Empty samplers use 255 for their texture index.
                    mtrlBytes.Add((byte)255);
                    mtrlBytes.AddRange(new byte[3]);
                }
                else
                {
                    mtrlBytes.AddRange(BitConverter.GetBytes(tex.Sampler.SamplerIdRaw));
                    mtrlBytes.AddRange(BitConverter.GetBytes(tex.Sampler.SamplerSettingsRaw));
                    mtrlBytes.Add((byte)i);
                    mtrlBytes.AddRange(new byte[3]);

                    // These have their secondary sampler also written when used with 2x uv layers.
                    if (xivMtrl.UvMapStrings.Count > 1)
                    {
                        if (tex.Sampler.SamplerId == ESamplerId.g_SamplerColorMap0
                        || tex.Sampler.SamplerId == ESamplerId.g_SamplerSpecularMap0
                        || tex.Sampler.SamplerId == ESamplerId.g_SamplerNormalMap0)
                        {
                            ESamplerId secondarySampler;
                            switch (tex.Sampler.SamplerId)
                            {
                                case ESamplerId.g_SamplerColorMap0:
                                    secondarySampler = ESamplerId.g_SamplerColorMap1;
                                    break;
                                case ESamplerId.g_SamplerSpecularMap0:
                                    secondarySampler = ESamplerId.g_SamplerSpecularMap1;
                                    break;
                                case ESamplerId.g_SamplerNormalMap0:
                                default:
                                    secondarySampler = ESamplerId.g_SamplerNormalMap1;
                                    break;
                            }

                            if (xivMtrl.Textures.Any(x => x.Sampler != null && x.Sampler.SamplerId == secondarySampler))
                            {
                                // This already has another copy of this sampler manually added on a different tex.
                                continue;
                            }

                            mtrlBytes.AddRange(BitConverter.GetBytes((uint)secondarySampler));
                            mtrlBytes.AddRange(BitConverter.GetBytes(tex.Sampler.SamplerSettingsRaw));
                            mtrlBytes.Add((byte)i);
                            mtrlBytes.AddRange(new byte[3]);
                        }
                    }
                }
            }
        }

        var shaderBytes = new List<byte>();
        foreach (var shaderParam in xivMtrl.ShaderConstants)
        {
            foreach (var f in shaderParam.Values)
            {
                shaderBytes.AddRange(BitConverter.GetBytes(f));
            }
        }

        // Pad out if we're missing anything.
        if (shaderBytes.Count < xivMtrl.ShaderConstantsDataSize)
        {
            shaderBytes.AddRange(new byte[xivMtrl.ShaderConstantsDataSize - shaderBytes.Count]);
        }
        mtrlBytes.AddRange(shaderBytes);

        // Backfill the header data.
        var fileSize = (short)mtrlBytes.Count;
        ReplaceBytesAt(mtrlBytes, BitConverter.GetBytes(fileSize), fileSizePointer);
        ReplaceBytesAt(mtrlBytes, BitConverter.GetBytes((ushort)stringBlock.Count), materialDataSizePointer);
        ReplaceBytesAt(mtrlBytes, BitConverter.GetBytes(shaderNamePointer), shaderNamePointerPointer);
        return mtrlBytes.ToArray();
    }

    // IOUtil.ReadNullTerminatedString(br, utf8: true)
    private static string ReadNullTerminatedString(BinaryReader br)
    {
        var data = new List<byte>();
        var b = br.ReadByte();
        while (b != 0)
        {
            data.Add(b);
            b = br.ReadByte();
        }
        return Encoding.UTF8.GetString(data.ToArray());
    }

    // Dat.Pad<T>(List<T> data, int paddingTarget, bool forcePadding = false)
    private static void Pad<T>(List<T> data, int paddingTarget, bool forcePadding = false)
    {
        var pad = paddingTarget - (data.Count % paddingTarget);
        if (pad == paddingTarget && !forcePadding)
        {
            return;
        }
        data.AddRange(new T[pad]);
    }

    // IOUtil.ReplaceBytesAt(List<byte> original, byte[] toInject, int index)
    private static void ReplaceBytesAt(List<byte> original, byte[] toInject, int index)
    {
        for (var i = 0; i < toInject.Length; i++)
        {
            original[index + i] = toInject[i];
        }
    }
}
