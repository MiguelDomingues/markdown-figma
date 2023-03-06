using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Renderers;
using Markdig.Syntax;
using Serilog;
using Svg;
using YamlDotNet.Serialization;

namespace MarkdownFigma
{
    class ImageUtils
    {

        public static double GetSimilarity(Stream originalImage, Stream otherImage)
        {
            var hashAlgorithm = new AverageHash();

            ulong hash1 = hashAlgorithm.Hash(originalImage);
            ulong hash2 = hashAlgorithm.Hash(otherImage);

            double percentageImageSimilarity = CompareHash.Similarity(hash1, hash2);
            return percentageImageSimilarity;
        }

        public static byte[] svg2png(byte[] svg)
        {
            using Stream svgStream = new MemoryStream(svg);

            SvgDocument doc = Svg.SvgDocument.Open<SvgDocument>(svgStream);

            using Bitmap bmp = doc.Draw();
            using MemoryStream pngStream = new MemoryStream();
            bmp.Save(pngStream, ImageFormat.Png);

            return pngStream.ToArray();
        }
    }
}