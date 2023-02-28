using System.Collections.Generic;
using Newtonsoft.Json;

namespace MarkdownFigma.Figma
{

    public class FigmaNode
    {

        [JsonProperty("document")]
        public FigmaDocument Document { get; internal set; }

    }
}