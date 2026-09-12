// Port of the image and power-of-two helpers from xivModdingFramework Helpers/IOUtil.cs (TexTools, GPL-3.0).

using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace DawntrailReady.Core.Textures;

public static class IOUtil
{
    public static byte[] GetImageSharpPixels(Image<Bgra32> img)
    {
        var mg = img.GetPixelMemoryGroup();
        using var ms = new MemoryStream();
        foreach (var g in mg)
        {
            var data = MemoryMarshal.AsBytes(g.Span).ToArray();
            ms.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    public static byte[] GetImageSharpPixels(Image<Rgba32> img)
    {
        var mg = img.GetPixelMemoryGroup();
        using var ms = new MemoryStream();
        foreach (var g in mg)
        {
            var data = MemoryMarshal.AsBytes(g.Span).ToArray();
            ms.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    public static bool IsPowerOfTwo(long x)
    {
        return IsPowerOfTwo((ulong)x);
    }

    public static bool IsPowerOfTwo(ulong x)
    {
        return (x != 0) && ((x & (x - 1)) == 0);
    }

    public static int RoundToPowerOfTwo(int x)
    {
        var min = FloorPower2(x);
        var max = CeilPower2(x);

        return max - x < x - min ? max : min;
    }

    public static int CeilPower2(int x)
    {
        if (x < 2)
        {
            return 1;
        }
        return (int)Math.Pow(2, (int)Math.Log(x - 1, 2) + 1);
    }

    public static int FloorPower2(int x)
    {
        if (x < 1)
        {
            return 1;
        }
        return (int)Math.Pow(2, (int)Math.Log(x, 2));
    }
}
