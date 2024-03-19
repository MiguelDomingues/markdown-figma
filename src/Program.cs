using System;
using System.ComponentModel.DataAnnotations;
using McMaster.Extensions.CommandLineUtils;
using Serilog;
using System.IO;
using System.Collections;
using Serilog.Events;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static MarkdownFigma.Figma.MarkdownFigmaSettings;

namespace MarkdownFigma
{
    class Program
    {
        public static int Main(string[] args)
            => CommandLineApplication.Execute<Program>(args);

        [Option(Template = "--input", Description = "The directory to scan for files or a file")]
        [Required]
        public string Input { get; }

        [Option(Template = "--pattern", Description = "File name pattern. Ignored if input is a file.")]
        public string FilePattern { get; }

        [Option(Template = "--export", Description = "Export folder name")]
        [Required]
        public string ExportFolder { get; } = "figma";

        [Option(Template = "--empty-export-folder", Description = "Export export directory before updating images")]
        public bool EmptyExportFolder { get; } = false;

        [Option(Template = "--api-token", Description = "Figma API Token")]
        [Required]
        public string FigmaToken { get; }

        [Option(Template = "--ignore-duplicates", Description = "Do not fail more than one element with the same name exists.")]
        public bool IgnoreDuplicates { get; } = false;

        [Option(Template = "--svg-visual-check-only", Description = "Only perform visual similarity check of SVG files.")]
        public bool SVGVisualCheckOnly { get; } = false;

        [Option(Template = "--similarity", Description = "When visual comparison is used, only images below this threshold are updated. Value between 0-100")]
        public double SimilarityThreshold { get; private set; } = 95.0;

        [Option("--max-updates", "Stop scanning files after N number of updates.", CommandOptionType.SingleValue)]
        public int MaxUpdates { get; private set; } = 0;

        [Option("--report", "File to output the markdown report", CommandOptionType.SingleValue)]
        public string ReportFile { get; private set; } = null;
        public StreamWriter Report { get; private set; } = null;

        [Option("--report-append", "Append to the report file if it exists.", CommandOptionType.NoValue)]
        public bool ReportAppend { get; private set; } = false;

        [Option("--parse-html", "Parse HTML when searching for images.", CommandOptionType.NoValue)]
        public bool ParseHTML { get; private set; } = false;

        [Option("--no-delete", "Do not delete images.", CommandOptionType.NoValue)]
        public bool NoDelete { get; private set; } = false;

        private Dictionary<string, IEnumerable<UpdateReport>> Updates = new Dictionary<string, IEnumerable<UpdateReport>>();

        private int OnExecute()
        {
            SetupLogger();

            Log.Information("Input: {Directory}", Input);
            Log.Information("File pattern is: {FilePattern}", FilePattern);
            Log.Information("Export folder name set to: {Folder}", ExportFolder);
            if (ReportFile != null)
                Report = new StreamWriter(ReportFile, ReportAppend);

            try
            {
                FileAttributes attr = File.GetAttributes(Input);

                if (attr.HasFlag(FileAttributes.Directory))
                    ScanDirectory(Input);
                else
                    ProcessFile(Input);

                if (Report != null)
                {
                    if (!ReportAppend)
                    {
                        Report.WriteLine("**Summary:**");
                        Report.WriteLine();
                        Report.WriteLine("Downloaded files: " + FigmaAPI.DOWNLOADS_COUNT);
                        Report.WriteLine();
                        Report.WriteLine("Downloaded size: " + BytesToString(FigmaAPI.DOWNLOADS_SIZE));
                    }

                    Report.Flush();
                    Report.Close();
                }
                return 0;
            }
            catch (Exception e)
            {
                Log.Error(e.Message);
                Log.Debug(e.StackTrace);
                return -1;
            }
        }

        private void SetupLogger()
        {
            string consoleTemplate = "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj} {NewLine}{Exception}";
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    outputTemplate: consoleTemplate)
                .WriteTo.File(
                    "figma-exporter.log",
                    fileSizeLimitBytes: 4 * 1014 * 1024,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    retainedFileCountLimit: 3,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} | [{Level:u3}] {Message:lj} {NewLine}{Exception}")
                .CreateLogger();
        }

        private void ScanDirectory(string path)
        {
            if (AboveMaxUpdates())
                return;
            Log.Information("Scanning directory {Path}", path);

            Log.Debug("Retrieving files from {Path}", path);
            string[] fileEntries = Directory.GetFiles(path, FilePattern);
            Array.Sort(fileEntries);
            foreach (string fileName in fileEntries)
                ProcessFile(fileName);

            // Recurse into subdirectories of this directory.
            string[] subdirectoryEntries = Directory.GetDirectories(path);
            Array.Sort(subdirectoryEntries);
            foreach (string subdirectory in subdirectoryEntries)
                ScanDirectory(subdirectory);
        }

        public void ProcessFile(string filePath)
        {
            if (AboveMaxUpdates())
                return;

            string fileName = Path.GetFileName(filePath);
            Log.Information("Processing file {File}", filePath);
            string figmaURL = MarkdownUtils.GetFrontMatterProperty(filePath);
            if (figmaURL == null)
            {
                Log.Debug("Figma url not found, skipping.");
                return;
            }
            Log.Information("Figma URL is: {URL}", figmaURL);
            string exportPath = Path.Combine(Path.GetDirectoryName(filePath), ExportFolder);

            IEnumerable<string> images = MarkdownUtils.GetImages(filePath, ExportFolder, ParseHTML);

            IEnumerable<UpdateReport> updatedAssets = FigmaAPI.ExportNodesTo(FigmaToken, figmaURL, exportPath, IgnoreDuplicates, SVGVisualCheckOnly, images, SimilarityThreshold);
            Log.Information("Downloaded {Count} files, totaling {Size}", FigmaAPI.DOWNLOADS_COUNT, BytesToString(FigmaAPI.DOWNLOADS_SIZE));

            if (Directory.Exists(exportPath))
            {
                if (EmptyExportFolder)
                {
                    List<UpdateReport> deletedAssets = new List<UpdateReport>();
                    Log.Information("Deleting files from {Path}", exportPath);
                    string[] fileEntries = Directory.GetFiles(exportPath);
                    foreach (string f in fileEntries)
                    {
                        string fname = Path.GetFileName(f);
                        if (!deletedAssets.Any(ua => ua.Name == fname))
                        {
                            if (updatedAssets.Any(ua => ua.Name == fname))
                            {
                                continue;
                            }
                            else if (images.Any(i => i == fname))
                            {
                                if (!Enum.GetValues(typeof(FigmaFormat)).Cast<FigmaFormat>().Any(ff => Enum.GetName(typeof(FigmaFormat), ff).ToLower() == Path.GetExtension(fname).Substring(1)))
                                {
                                    Log.Information("Ignoring {File} due to its extension.", fname);
                                }
                                else
                                {
                                    deletedAssets.Add(new UpdateReport()
                                    {
                                        Name = fname,
                                        Similarity = 0,
                                        Action = UpdateAction.FIGMA_MISSING,
                                    });
                                }
                            }
                            else
                            {
                                Log.Debug("Deleting file {File}", f);
                                if (NoDelete == false)
                                {
                                    File.Delete(f);
                                    deletedAssets.Add(new UpdateReport()
                                    {
                                        Name = fname,
                                        Similarity = 0,
                                        Action = UpdateAction.DELETE,
                                    });
                                }
                            }
                        }

                    }
                    updatedAssets = updatedAssets.Concat(deletedAssets).ToList();
                }
                else
                {
                    IEnumerable<UpdateReport> missingInFigma = images.Where(s => !updatedAssets.Any(ua => ua.Name == s)).Select(s =>
                        new UpdateReport()
                        {
                            Name = s,
                            Similarity = 0,
                            Action = UpdateAction.FIGMA_MISSING,
                        });
                    updatedAssets = updatedAssets.Concat(missingInFigma.ToList());
                }
            }

            Updates.Add(filePath, updatedAssets);

            if (updatedAssets.Count() > 0 && Report != null)
            {
                if (updatedAssets.Any(ua => ua.Action != UpdateAction.NONE))
                {
                    Report.WriteLine(":memo: " + filePath + " ([Figma](" + figmaURL + "))");
                    Report.WriteLine();
                    Report.WriteLine("Visual Asset | Status");
                    Report.WriteLine("------------ | ------");

                    foreach (UpdateReport ur in updatedAssets)
                    {
                        switch (ur.Action)
                        {
                            case UpdateAction.HIDDEN:
                                Report.WriteLine("[" + ExportFolder + Path.DirectorySeparatorChar + ur.Name + "](" + ur.URL + ")" + " | Hidden");
                                break;
                            case UpdateAction.DUPLICATE:
                                Report.WriteLine("[" + ExportFolder + Path.DirectorySeparatorChar + ur.Name + "](" + ur.URL + ")" + " | Duplicated");
                                break;
                            case UpdateAction.NOT_TOP_LEVEL:
                                Report.WriteLine("[" + ExportFolder + Path.DirectorySeparatorChar + ur.Name + "](" + ur.URL + ")" + " | Not at top-level");
                                break;
                            case UpdateAction.UPDATE_SIMILARITY:
                                Report.WriteLine("[" + ExportFolder + Path.DirectorySeparatorChar + ur.Name + "](" + ur.URL + ")" + " | Similarity @ " + ur.Similarity.ToString("0.##") + " %");
                                break;
                            case UpdateAction.UPDATE:
                                Report.WriteLine("[" + ExportFolder + Path.DirectorySeparatorChar + ur.Name + "](" + ur.URL + ")" + " | Update");
                                break;
                            case UpdateAction.DELETE:
                                Report.WriteLine(ExportFolder + Path.DirectorySeparatorChar + ur.Name + " | Delete");
                                break;
                            case UpdateAction.FIGMA_MISSING:
                                Report.WriteLine(ExportFolder + Path.DirectorySeparatorChar + ur.Name + " | Missing in Figma");
                                break;
                            case UpdateAction.UNUSED:
                                Report.WriteLine("[" + ExportFolder + Path.DirectorySeparatorChar + ur.Name + "](" + ur.URL + ")" + " | Not used");
                                break;
                            case UpdateAction.NONE:
                                break;
                        }
                    }
                    Report.WriteLine();
                }
            }
        }

        private bool AboveMaxUpdates()
        {
            if (MaxUpdates <= 0)
                return false;

            bool fileChanges = Updates.SelectMany(kv => kv.Value).Any(u => u.Action == UpdateAction.DELETE || u.Action == UpdateAction.UPDATE_SIMILARITY || u.Action == UpdateAction.UPDATE);

            IEnumerable<UpdateReport> updates = Updates.SelectMany(kv => kv.Value).Where(u => u.Action != UpdateAction.NONE);
            if (updates.Count() > MaxUpdates && fileChanges)
                return true;

            return false;
        }

        static String BytesToString(long byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
            if (byteCount == 0)
                return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 2);
            return (Math.Sign(byteCount) * num).ToString() + " " + suf[place];
        }
    }
}
