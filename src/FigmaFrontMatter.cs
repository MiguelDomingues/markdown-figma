using YamlDotNet.Serialization;

namespace MarkdownFigma
{
    public class FigmaFrontMatter
    {
        [YamlMember(Alias = "figma", ApplyNamingConventions = false)]
        public string Figma { get; internal set; }

    }
}