// Port of xivModdingFramework Mods/EndwalkerUpgrade.cs ConvertEyeMaskToDiffuse (lines 1910-2003, TexTools, GPL-3.0).
// TexTools reads chara/common/texture/eye/eye01_base.tex and eye01_mask.tex (original game files) inside the
// function; here the caller passes them in.

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DawntrailReady.Core.Textures;

public static class EyeMask
{
    /// <summary>
    /// Takes the raw data of an old Endwalker mask image, and converts it into a Dawntrail style diffuse.
    /// This only makes use of the Mask.Red channel data, all other data is discarded as it cannot be replicated.
    /// Mutates <paramref name="maskData"/> like TexTools does.
    /// </summary>
    /// <param name="baseDiffuseTex">The original game's chara/common/texture/eye/eye01_base.tex.</param>
    /// <param name="frameTex">The original game's chara/common/texture/eye/eye01_mask.tex.</param>
    public static async Task<(byte[] PixelData, int Width, int Height)> ConvertEyeMaskToDiffuse(byte[] maskData, int originalMaskWidth,
        int originalMaskHeight, XivTex baseDiffuseTex, XivTex frameTex)
    {
        // The Ratio of Iris to Sclera is 92/100 in old textures, but is
        // 1/2.55 roughly in the new.
        // Multiplying these terms together results in a ratio of roughly .44
        double ratio = 0.442;

        // In order to guarantee we're resizing up, not down, and are still a power-of-two, we have to 4x the
        // dimensions of the original mask file, as a 2x would result in some amount of compression.
        var w = originalMaskWidth * 4;
        var h = originalMaskHeight * 4;

        var irisW = (int)(w * ratio);
        var irisH = (int)(h * ratio);

        var diffuseData = await baseDiffuseTex.GetRawPixels();
        var frameData = await frameTex.GetRawPixels();

        // Convert mask to greyscale copy of just the red channel data.
        await TextureHelpers.ExpandChannel(maskData, 0, originalMaskWidth, originalMaskHeight);
        var resizedMask = await TextureHelpers.ResizeImage(maskData, originalMaskWidth, originalMaskHeight, irisW, irisH);

        // Convert eye frame to just the actual framing information
        await TextureHelpers.ExpandChannel(frameData, 2, frameTex.Width, frameTex.Height, true);

        // Resize and blur the frame slightly.
        using (var frameImage = Image.LoadPixelData<Rgba32>(frameData, frameTex.Width, frameTex.Height))
        {
            var resizeOptions = new ResizeOptions
            {
                Size = new Size(w, h),
                PremultiplyAlpha = false,
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.NearestNeighbor,
            };
            frameImage.Mutate(x => x.Resize(resizeOptions));

            // Box-blur the mask just a hair to reduce the harshness at the edges.
            // This looks a little nicer than just bicubic upscaling the mask.
            frameImage.Mutate(x => x.BoxBlur(w / 128));
            frameData = IOUtil.GetImageSharpPixels(frameImage);
        }

        var maskPixels = new byte[w * h * 4];

        // Draw the mask onto a new blank canvas and get the byte data back.
        using (var blankImage = Image.LoadPixelData<Rgba32>(maskPixels, w, h))
        {
            using (var maskImage = Image.LoadPixelData<Rgba32>(resizedMask, irisW, irisH))
            {
                var pt = new Point((w / 2) - (irisW / 2), (h / 2) - (irisH / 2));
                blankImage.Mutate(x => x.DrawImage(maskImage, pt, 1.0f));

                maskPixels = IOUtil.GetImageSharpPixels(blankImage);
            }
        }

        // Use the frame to mask the mask.
        await TextureHelpers.MaskImage(maskPixels, frameData, w, h);

        // And finally, resize the diffuse and draw the masked image back in.
        using (var mainImage = Image.LoadPixelData<Rgba32>(diffuseData, baseDiffuseTex.Width, baseDiffuseTex.Height))
        {
            using (var maskImage = Image.LoadPixelData<Rgba32>(maskPixels, w, h))
            {
                var resizeOptions = new ResizeOptions
                {
                    Size = new Size(w, h),
                    PremultiplyAlpha = false,
                    Mode = ResizeMode.Stretch,
                    Sampler = KnownResamplers.Bicubic,
                };
                mainImage.Mutate(x => x.Resize(resizeOptions));

                var ops = new GraphicsOptions()
                {
                    AlphaCompositionMode = PixelAlphaCompositionMode.SrcAtop,
                };
                mainImage.Mutate(x => x.DrawImage(maskImage, ops));

                var finalData = IOUtil.GetImageSharpPixels(mainImage);
                return (finalData, mainImage.Width, mainImage.Height);
            }
        }
    }
}
