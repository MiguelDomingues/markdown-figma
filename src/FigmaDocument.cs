using System.Collections.Generic;
using Newtonsoft.Json;

namespace MarkdownFigma.Figma
{

    public class FigmaDocument
    {

        [JsonProperty("name")]
        public string Name { get; internal set; }

        [JsonProperty("children")]
        public List<FigmaChild> Children { get; internal set; }

    }
}