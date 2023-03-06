using System.IO;
using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using SkiaSharp;
using Svg.Skia;

namespace MarkdownFigma
{
    class ImageUtils
    {

        public static double GetSimilarity(byte[] originalImage, byte[] otherImage)
        {
            using (MemoryStream original = new MemoryStream(originalImage))
            using (MemoryStream other = new MemoryStream(otherImage))
            {
                return GetSimilarity(original, other);
            }
        }

        private static double GetSimilarity(Stream originalImage, Stream otherImage)
        {
            var hashAlgorithm = new AverageHash();

            ulong hash1 = hashAlgorithm.Hash(originalImage);
            ulong hash2 = hashAlgorithm.Hash(otherImage);

            double percentageImageSimilarity = CompareHash.Similarity(hash1, hash2);
            return percentageImageSimilarity;
        }

        public static byte[] svg2png(byte[] svgArray)
        {
            using (var svg = new SKSvg())
            using (MemoryStream svgStream = new MemoryStream(svgArray))
            {
                if (svg.Load(svgStream) is { })
                {
                    using (var stream = new MemoryStream())
                    {
                        svg.Picture.ToImage(stream,
                            background: SKColors.Empty,
                            format: SKEncodedImageFormat.Png,
                            quality: 90,
                            scaleX: 1f,
                            scaleY: 1f,
                            skColorType: SKImageInfo.PlatformColorType,
                            skAlphaType: SKAlphaType.Unpremul,
                            skColorSpace: SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.Srgb));
                        return stream.ToArray();
                    }
                }
                else
                {
                    throw new System.Exception("Failed to convert to png");
                }
            }
        }
    }
}