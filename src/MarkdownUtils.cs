using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Serilog;
using YamlDotNet.Serialization;

namespace MarkdownFigma
{
    class MarkdownUtils
    {
        internal static string GetFrontMatterProperty(string file)
        {
            string frontMatter = GetFrontMatter(file);
            if (frontMatter == null)
            {
                Log.Debug("No front matter found in file {File}", file);
                return null;
            }
            var yaml = ParseYaml(frontMatter);

            return yaml.Figma;
        }

        static FigmaFrontMatter ParseYaml(string yaml)
        {
            var deserializer = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();
            return deserializer.Deserialize<FigmaFrontMatter>(yaml);
        }

        static string GetFrontMatter(string file)
        {
            string markdown = File.ReadAllText(file);

            MarkdownPipeline pipeline = GetPipeline();

            MarkdownDocument document = Markdown.Parse(markdown, pipeline);
            var yamlBlock = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();

            if (yamlBlock != null)
            {
                string yaml = yamlBlock.Lines.ToString();

                return yaml;
            }

            return null;
        }

        private static MarkdownPipeline GetPipeline()
        {
            var pipeline = new MarkdownPipelineBuilder()
                            .UseYamlFrontMatter()
                            .Build();

            StringWriter writer = new StringWriter();
            var renderer = new HtmlRenderer(writer);
            pipeline.Setup(renderer);
            return pipeline;
        }

        internal static IEnumerable<string> GetImages(string file, string pathFilter, bool parseHTML)
        {
            string markdown = File.ReadAllText(file);

            MarkdownPipeline pipeline = GetPipeline();

            MarkdownDocument document = Markdown.Parse(markdown, pipeline);

            List<string> images = new List<string>();

            string basePath = Path.GetDirectoryName(file);
            pathFilter = Path.GetFullPath(Path.Combine(basePath, pathFilter));
            foreach (LinkInline i in document.Descendants<LinkInline>().Where(li => li.IsImage))
            {
                string imagePath = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(basePath, i.Url)));
                if (imagePath.Equals(pathFilter))
                    images.Add(Path.GetFileName(i.Url));
                else
                    Log.Debug("Image {Image} will be ignored. Outside of path filter.", i.Url);
            }

            if (parseHTML)
                images = images.Concat(ExtractHTMLImages(basePath, pathFilter, document)).ToList();

            IEnumerable<string> withoutQueryString = images.Select(img => IOUtils.RemoveQueryString(img));

            return withoutQueryString;
        }

        private static IEnumerable<string> ExtractHTMLImages(string basePath, string pathFilter, MarkdownDocument document)
        {
            List<string> images = new List<string>();
            var htmlObjs = document.Descendants().Where(d => d is HtmlBlock).Cast<HtmlBlock>();
            foreach (HtmlBlock obj in htmlObjs)
            {
                string rawHtml = String.Join("\r\n", obj.Lines);

                HtmlDocument htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(rawHtml);

                if (htmlDoc == null)
                    continue;

                IEnumerable<HtmlNode> imageNodes = htmlDoc.DocumentNode.Descendants("img");
                if (imageNodes.Count() > 0)
                {
                    foreach (HtmlNode img in imageNodes)
                    {
                        string imgSrc = img.GetAttributeValue("src", null);
                        string imagePath = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(basePath, imgSrc)));
                        if (imagePath.Equals(pathFilter))
                            images.Add(Path.GetFileName(imgSrc));
                        else
                            Log.Debug("Image {Image} will be ignored. Outside of path filter.", imgSrc);

                    }
                }

                // Markdig treats blocks like <table>...</table> as a single HtmlBlock and
                // doesn't parse markdown syntax inside them, even when the block opts in
                // (e.g. <table markdown="1">). Scan the raw HTML for markdown image
                // syntax so those references aren't missed.
                foreach (Match m in MarkdownImageRegex.Matches(rawHtml))
                {
                    string imgSrc = m.Groups["url"].Value;
                    if (string.IsNullOrEmpty(imgSrc))
                        continue;

                    string imagePath = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(basePath, imgSrc)));
                    if (imagePath.Equals(pathFilter))
                        images.Add(Path.GetFileName(imgSrc));
                    else
                        Log.Debug("Image {Image} will be ignored. Outside of path filter.", imgSrc);
                }
            }
            return images;
        }

        private static readonly Regex MarkdownImageRegex = new Regex(
            @"!\[[^\]]*\]\((?<url>[^\s)]+)(?:\s+(?:""[^""]*""|'[^']*'|\([^)]*\)))?\)",
            RegexOptions.Compiled);

    }
}