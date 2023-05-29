using System.IO;
using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using SkiaSharp;
using Svg.Skia;

namespace MarkdownFigma
{
    class IOUtils
    {

        public static string RemoveQueryString(string path)
        {
            int index = path.IndexOf("?");
            if (index <= 0)
                return path;

            return path.Substring(0, index);
        }

    }
}