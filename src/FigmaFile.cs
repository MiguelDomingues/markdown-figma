using System.Collections.Generic;
using Newtonsoft.Json;

namespace MarkdownFigma.Figma
{

    public class FigmaFile
    {

        [JsonProperty("nodes")]
        public Dictionary<string, FigmaNode> Nodes { get; internal set; }

    }
}