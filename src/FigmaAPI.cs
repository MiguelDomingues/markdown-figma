using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using MarkdownFigma.Figma;
using Newtonsoft.Json;
using Serilog;
using static MarkdownFigma.Figma.MarkdownFigmaSettings;

namespace MarkdownFigma
{
    class FigmaAPI
    {
        private static readonly string FIGMA_API_HEADER = "X-Figma-Token";
        private static readonly string FIGMA_API_ENDPOINT = "https://api.figma.com/v1/";

        public static int DOWNLOADS_COUNT = 0;
        public static int DOWNLOADS_SIZE = 0;

        public static int NUMBER_OF_THREADS { get; private set; } = Math.Max(Environment.ProcessorCount, 4);
        public static int WAIT_MS_TOO_MANY_REQUESTS { get; } = 20000;

        private static HttpClient CLIENT = null;

        private static HttpClient GetHTTPClient(string token)
        {
            if (CLIENT == null)
            {
                HttpClient client = new HttpClient();
                client.BaseAddress = new Uri(FIGMA_API_ENDPOINT);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                client.DefaultRequestHeaders.Add(FIGMA_API_HEADER, token);
                CLIENT = client;
            }
            return CLIENT;
        }

        private static T Get<T>(string token, string path, string query, int retries = 3)
        {
            try
            {
                HttpClient client = GetHTTPClient(token);

                UriBuilder builder = new UriBuilder(client.BaseAddress + path);
                if (query != null && query != "")
                    builder.Query = query;
                Log.Debug("GET " + builder.Path + builder.Query);

                using (HttpResponseMessage response = client.GetAsync(builder.Uri).GetAwaiter().GetResult())
                {
                    string rawResponse = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    Log.Debug("RESPONSE: {JSON}", rawResponse);
                    if (response.IsSuccessStatusCode)
                        return JsonConvert.DeserializeObject<T>(rawResponse);
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        Log.Warning("Too Many Requests received from Figma API. Sleeping...");
                        Thread.Sleep(WAIT_MS_TOO_MANY_REQUESTS);
                        return Get<T>(token, path, query, retries - 1);
                    }
                    else
                        throw new Exception("Figma API Error: " + rawResponse);
                }
            }
            catch (Exception e)
            {
                if (retries > 0)
                {
                    Log.Warning("Request failed with {Error}. Retrying...", e.Message);
                    return Get<T>(token, path, query, retries - 1);
                }
                else
                    throw;
            }
        }

        private static FigmaNode GetFileNode(string token, string key, string nodeId)
        {
            FigmaFile f = Get<FigmaFile>(token, $"files/{key}/nodes?ids={nodeId}", null);
            FigmaNode node;
            f.Nodes.TryGetValue(nodeId, out node);
            if (node == null)
            {
                throw new Exception("The node (page) " + nodeId + " was not found in file with key " + key);
            }
            return node;
        }

        private static Dictionary<string, string> GetExportUrls(string token, string key, IEnumerable<string> ids, FigmaFormat format)
        {
            try
            {
            Log.Information("Obtaining download urls for {Count} {Format} elements...", ids.Count(), format);
                string idsStr = string.Join(",", ids);
                FigmaImagesExport f = Get<FigmaImagesExport>(token, $"images/{key}?ids={idsStr}&format={format.ToString().ToLower()}", null);

                return f.Images;
            }
            catch
            {
                Log.Warning("Export using single request failed. Switching to independent export mode...");
                return GetExportUrlsParallel(token, key, ids, format);
            }
        }

        private static Dictionary<string, string> GetExportUrlsParallel(string token, string key, IEnumerable<string> ids, FigmaFormat format)
        {
            ConcurrentDictionary<string, string> result = new ConcurrentDictionary<string, string>();
            ids.AsParallel().WithDegreeOfParallelism(NUMBER_OF_THREADS).ForAll(id =>
            {
                FigmaImagesExport f = Get<FigmaImagesExport>(token, $"images/{key}?ids={id}&format={format.ToString().ToLower()}", null);
                foreach (KeyValuePair<string, string> i in f.Images)
                {
                    result.AddOrUpdate(i.Key, i.Value, (key, oldValue) => i.Value);
                }
            });

            return result.ToDictionary(entry => entry.Key, entry => entry.Value);
        }

        internal static IEnumerable<UpdateReport> ExportNodesTo(string figmaToken, string figmaURL, string exportPath, bool ignoreDuplicates, bool svgVisualCheckOnly, IEnumerable<string> includeOnly, double threshold, IEnumerable<string> IgnorePaths)
        {
            Log.Information("Inspecting {Url}", figmaURL);
            string fileKey = figmaURL.Substring(figmaURL.IndexOf("figma.com/file") + 15);
            fileKey = fileKey.Substring(0, fileKey.IndexOf("/"));
            ConcurrentBag<UpdateReport> updatedAssets = new();
            string nodeId = GetFigmaNodeId(figmaURL);

            FigmaNode node = GetFileNode(figmaToken, fileKey, nodeId);

            IEnumerable<FigmaChild> descendants = node.Document.Children.SelectRecursive(c => c.Children).Where(c => c.ExportSettings != null);

            IEnumerable<string> topLevelNodeIds = node.Document.Children.Select(c => c.Id);
            foreach (FigmaChild fg in descendants.Where(c => !topLevelNodeIds.Any(tl => tl == c.Id)))
            {
                foreach (MarkdownFigmaSettings exp in fg.ExportSettings)
                {
                    Log.Warning("{Name} is not defined at the top-level {URL}", fg.Name + "." + Enum.GetName(typeof(FigmaFormat), exp.Format).ToLower(), GetFigmaURL(fileKey, fg.Id));
                    updatedAssets.Add(new UpdateReport()
                    {
                        Name = fg.Name + "." + Enum.GetName(typeof(FigmaFormat), exp.Format).ToLower(),
                        Action = UpdateAction.NOT_TOP_LEVEL,
                        URL = GetFigmaURL(fileKey, fg.Id),
                    });
                }
            }

            IEnumerable<FigmaChild> dupExport = descendants.Where(c => c.ExportSettings.Count() > 1);
            if (dupExport.Count() > 0)
            {
                foreach (FigmaChild c in dupExport)
                {
                    Log.Warning("Multiple exports defined in element {Name} from {URL}", c.Name, figmaURL);
                }
            }

            IEnumerable<FigmaChild> hidden = descendants.Where(c => c.ExportSettings.Count() > 0 && !c.Visible);
            if (hidden.Count() > 0)
            {
                foreach (FigmaChild fg in hidden)
                {
                    foreach (MarkdownFigmaSettings exp in fg.ExportSettings)
                    {
                        Log.Warning("{Name} has exports but is hidden {URL}", fg.Name + "." + Enum.GetName(typeof(FigmaFormat), exp.Format).ToLower(), GetFigmaURL(fileKey, fg.Id));
                        updatedAssets.Add(new UpdateReport()
                        {
                            Name = fg.Name + "." + Enum.GetName(typeof(FigmaFormat), exp.Format).ToLower(),
                            Action = UpdateAction.HIDDEN,
                            URL = GetFigmaURL(fileKey, fg.Id),
                        });
                    }
                }
            }

            IEnumerable<FigmaChild> childs = node.Document.Children.Where(c => c.Visible && c.ExportSettings != null && c.ExportSettings.Count() > 0);
            Log.Information("Found {Count} elements to export.", childs.Count());

            foreach (FigmaFormat format in Enum.GetValues(typeof(FigmaFormat)))
            {
                string extension = "." + Enum.GetName(typeof(FigmaFormat), format).ToLower();
                ConcurrentDictionary<string, object> filenames = new ConcurrentDictionary<string, object>();
                IEnumerable<FigmaChild> formatChilds = childs.Where(c => c.ExportSettings.Any(e => e.Format == format));
                formatChilds = formatChilds.Where(c => includeOnly == null || includeOnly.Any(f => f == c.Name + extension));
                IEnumerable<FigmaChild> unused = childs.Where(c => c.ExportSettings.Any(e => e.Format == format)).Where(c => !formatChilds.Any(f => f.Id == c.Id));
                foreach (FigmaChild u in unused)
                {
                    Log.Warning("{Name} has an export defined but is not used. {URL}", u.Name + extension, GetFigmaURL(fileKey, u.Id));
                    updatedAssets.Add(new UpdateReport()
                    {
                        Name = u.Name + extension,
                        Action = UpdateAction.UNUSED,
                        URL = GetFigmaURL(fileKey, u.Id),
                    });
                }
                if (formatChilds.Count() == 0)
                    continue;
                Dictionary<string, string> downloadUrls = GetExportUrls(figmaToken, fileKey, formatChilds.Select(c => c.Id), format);
                downloadUrls.AsParallel().WithDegreeOfParallelism(NUMBER_OF_THREADS).ForAll(dl =>
                {
                    string name = formatChilds.Where(c => c.Id == dl.Key).First().Name.Trim();
                    string fixedName = ReplaceInvalidChars(name);
                    if (name != fixedName)
                    {
                        Log.Warning("Figma has element name set to '{Original}'. It will be changed to '{New}", name + extension, fixedName + extension);
                        name = fixedName;
                    }
                    if (name.Length == 0)
                        throw new Exception("Element " + dl.Key + " does not have a name defined. Check " + GetFigmaURL(fileKey, dl.Key));

                    if (!ignoreDuplicates && filenames.ContainsKey(name))
                    {
                        Log.Warning("Duplicated element with name '" + name + extension + "' at " + GetFigmaURL(fileKey, dl.Key));
                        updatedAssets.Add(new UpdateReport()
                        {
                            Name = name + extension,
                            Action = UpdateAction.DUPLICATE,
                            URL = GetFigmaURL(fileKey, dl.Key),
                        });
                    }
                    filenames.TryAdd(name, null);

                    string destination = IOUtils.UniformPath(Path.Combine(exportPath, name + extension));

                    if (IgnorePaths.Contains(destination))
                    {
                        Log.Information("File {Path} will be skipped due to being part of the ignored list.", destination);
                        updatedAssets.Add(new UpdateReport()
                        {
                            Name = name + extension,
                            Similarity = 0,
                            Action = UpdateAction.IGNORE,
                            URL = GetFigmaURL(fileKey, dl.Key),
                        });
                    }
                    else
                    {
                        Log.Information("Downloading {Name} ({Id}) from {Url}", name + extension, dl.Key, dl.Value);

                        byte[] originalFile = null;
                        if (File.Exists(destination))
                        {
                            originalFile = File.ReadAllBytes(destination);
                        }

                        byte[] newImage = DownloadFile(dl.Value, destination);
                        if (!File.Exists(destination))
                        {
                            Log.Information("Writing {Name} to {Path}", name + extension, destination);
                            File.WriteAllBytes(destination, newImage);
                        }
                        else if (format == FigmaFormat.SVG)
                        {
                            byte[] originalPNG = ImageUtils.svg2png(originalFile);
                            byte[] newPNG = ImageUtils.svg2png(newImage);
                            double similarity = ImageUtils.GetSimilarity(originalPNG, newPNG);
                            if (similarity < threshold)
                            {
                                Log.Information("Writing {Name} to {Path} since similarity is {Similarity} % (below threshold of {Threshold} %)", name + extension, destination, similarity, threshold);
                                File.WriteAllBytes(destination, newImage);
                                updatedAssets.Add(new UpdateReport()
                                {
                                    Name = name + extension,
                                    Similarity = similarity,
                                    Action = UpdateAction.UPDATE_SIMILARITY,
                                    URL = GetFigmaURL(fileKey, dl.Key),
                                });
                            }
                            else if (!svgVisualCheckOnly && !originalFile.SequenceEqual(newImage))
                            {
                                Log.Information("Writing {Name} to {Path}", name + extension, destination);
                                File.WriteAllBytes(destination, newImage);
                                updatedAssets.Add(new UpdateReport()
                                {
                                    Name = name + extension,
                                    Similarity = similarity,
                                    Action = UpdateAction.UPDATE,
                                    URL = GetFigmaURL(fileKey, dl.Key),
                                });
                            }
                            else
                            {
                                updatedAssets.Add(new UpdateReport()
                                {
                                    Name = name + extension,
                                    Similarity = similarity,
                                    Action = UpdateAction.NONE,
                                    URL = GetFigmaURL(fileKey, dl.Key),
                                });
                            }

                        }
                        else if (format == FigmaFormat.PNG)
                        {
                            double similarity = ImageUtils.GetSimilarity(originalFile, newImage);
                            if (similarity < threshold)
                            {
                                Log.Information("Writing {Name} to {Path} since similarity is {Similarity} % (below threshold of {Threshold} %)", name + extension, destination, similarity, threshold);
                                File.WriteAllBytes(destination, newImage);
                                updatedAssets.Add(new UpdateReport()
                                {
                                    Name = name + extension,
                                    Similarity = similarity,
                                    Action = UpdateAction.UPDATE_SIMILARITY,
                                    URL = GetFigmaURL(fileKey, dl.Key),
                                });
                            }
                            else
                            {
                                updatedAssets.Add(new UpdateReport()
                                {
                                    Name = name + extension,
                                    Similarity = similarity,
                                    Action = UpdateAction.NONE,
                                    URL = GetFigmaURL(fileKey, dl.Key),
                                });
                            }
                        }
                        else
                        {
                            throw new Exception("Unsupported format.");
                        }
                        DOWNLOADS_COUNT++;
                        DOWNLOADS_SIZE += newImage.Length;
                    }
                });
            }
            return updatedAssets;
        }

        private static string GetFigmaNodeId(string figmaURL)
        {
            string nodeId = figmaURL.Substring(figmaURL.IndexOf("node-id=") + 8);
            nodeId = HttpUtility.UrlDecode(nodeId);
            if (nodeId.Contains("&"))
                nodeId = nodeId.Substring(0, nodeId.IndexOf("&"));
            if (nodeId.Contains("-"))
                nodeId = nodeId.Replace('-', ':');
            return nodeId;
        }

        private static string ReplaceInvalidChars(string filename)
        {
            string regexSearch = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            Regex r = new Regex(string.Format("[{0}]", Regex.Escape(regexSearch)));
            return r.Replace(filename, "");
        }

        private static byte[] DownloadFile(string url, string file)
        {
            byte[] fileContent = HttpHelper.HttpGet(url, null);

            string directory = Path.GetDirectoryName(file);
            Directory.CreateDirectory(directory);

            return fileContent;
        }

        private static string GetFigmaURL(string fileKey, string nodeId)
        {
            return "https://www.figma.com/file/" + fileKey + "/?node-id=" + nodeId;
        }

    }
}