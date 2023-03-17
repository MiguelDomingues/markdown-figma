using System.Collections.Generic;
using Newtonsoft.Json;

namespace MarkdownFigma.Figma
{

    public class FigmaChild
    {

        [JsonProperty("id")]
        public string Id { get; internal set; }

        [JsonProperty("name")]
        public string Name { get; internal set; }

        [JsonProperty("visible")]
        public bool Visible { get; internal set; } = true;

        [JsonProperty("exportSettings")]
        public List<MarkdownFigmaSettings> ExportSettings { get; internal set; }

        [JsonProperty("children")]
        public List<FigmaChild> Children { get; internal set; }
    }
}