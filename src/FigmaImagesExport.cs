using System.Collections.Generic;
using Newtonsoft.Json;

namespace MarkdownFigma.Figma
{

    public class FigmaImagesExport
    {

        [JsonProperty("images")]
        public Dictionary<string, string> Images { get; internal set; }

    }
}