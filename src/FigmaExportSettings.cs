using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace MarkdownFigma.Figma
{

    public class MarkdownFigmaSettings
    {

        public enum FigmaFormat
        {

            [EnumMember(Value = "SVG")]
            SVG,
            [EnumMember(Value = "PNG")]
            PNG,
            [EnumMember(Value = "JPG")]
            JPG,
        }

        [JsonProperty("format")]
        public FigmaFormat Format { get; internal set; }

    }
}