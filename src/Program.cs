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

namespace MarkdownFigma
{
    class Program
    {
        public static int Main(string[] args)
            => CommandLineApplication.Execute<Program>(args);

        [Option(Template = "--input", Description = "The file to scan for files")]
        [Required]
        public string InputDirectory { get; }

        [Option(Template = "--pattern", Description = "File name pattern")]
        [Required]
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
        public StringBuilder Report { get; private set; } = null;

        private Dictionary<string, IEnumerable<UpdateReport>> Updates = new Dictionary<string, IEnumerable<UpdateReport>>();

        private int OnExecute()
        {
            SetupLogger();

            Log.Information("Scan directory: {Directory}", InputDirectory);
            Log.Information("File pattern is: {FilePattern}", FilePattern);
            Log.Information("Export folder name set to: {Folder}", ExportFolder);
            if (ReportFile != null)
                Report = new StringBuilder();

            try
            {
                ScanDirectory(InputDirectory);
                if (Report != null)
                {
                    Report.AppendLine("**Summary:**");
                    Report.AppendLine();
                    Report.AppendLine("Downloaded files: " + FigmaAPI.DOWNLOADS_COUNT);
                    Report.AppendLine();
                    Report.AppendLine("Downloaded size: " + BytesToString(FigmaAPI.DOWNLOADS_SIZE));

                    File.WriteAllText(ReportFile, Report.ToString());
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
            if (!AboveMaxUpdates())
                return;
            Log.Information("Scanning directory {Path}", path);

            Log.Debug("Retrieving files from {Path}", path);
            string[] fileEntries = Directory.GetFiles(path, FilePattern);
            foreach (string fileName in fileEntries)
                ProcessFile(fileName);

            // Recurse into subdirectories of this directory.
            string[] subdirectoryEntries = Directory.GetDirectories(path);
            foreach (string subdirectory in subdirectoryEntries)
                ScanDirectory(subdirectory);
        }

        public void ProcessFile(string filePath)
        {
            if (!AboveMaxUpdates())
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

            IEnumerable<string> images = MarkdownUtils.GetImages(filePath, ExportFolder);

            IEnumerable<UpdateReport> updatedAssets = FigmaAPI.ExportNodesTo(FigmaToken, figmaURL, exportPath, IgnoreDuplicates, SVGVisualCheckOnly, images, SimilarityThreshold);
            Log.Information("Downloaded {Count} files, totaling {Size}", FigmaAPI.DOWNLOADS_COUNT, BytesToString(FigmaAPI.DOWNLOADS_SIZE));

            if (EmptyExportFolder && Directory.Exists(exportPath))
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
                            deletedAssets.Add(new UpdateReport()
                            {
                                Name = fname,
                                Similarity = 0,
                                Action = UpdateAction.FIGMA_MISSING,
                            });
                        }
                        else
                        {
                            Log.Debug("Deleting file {File}", f);
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
                updatedAssets = updatedAssets.Concat(deletedAssets).ToList();
            }

            Updates.Add(filePath, updatedAssets);

            if (updatedAssets.Count() > 0 && Report != null)
            {
                if (updatedAssets.Any(ua => ua.Action != UpdateAction.NONE))
                {
                    Report.AppendLine(":memo: " + filePath + " ([Figma](" + figmaURL + "))");
                    Report.AppendLine();
                    Report.AppendLine("Visual Asset | Status");
                    Report.AppendLine("------------ | ------");

                    foreach (UpdateReport ur in updatedAssets)
                    {
                        switch (ur.Action)
                        {
                            case UpdateAction.UPDATE_SIMILARITY:
                                Report.AppendLine("[" + ExportFolder + Path.DirectorySeparatorChar + ur.Name + "](" + ur.URL + ")" + " | Similarity @ " + ur.Similarity.ToString("0.##") + " %");
                                break;
                            case UpdateAction.UPDATE:
                                Report.AppendLine("[" + ExportFolder + Path.DirectorySeparatorChar + ur.Name + "](" + ur.URL + ")" + " | Update");
                                break;
                            case UpdateAction.DELETE:
                                Report.AppendLine(ExportFolder + Path.DirectorySeparatorChar + ur.Name + " | Delete");
                                break;
                            case UpdateAction.FIGMA_MISSING:
                                Report.AppendLine(ExportFolder + Path.DirectorySeparatorChar + ur.Name + " | Missing in Figma");
                                break;
                            case UpdateAction.UNUSED:
                                Report.AppendLine("[" + ExportFolder + Path.DirectorySeparatorChar + ur.Name + "](" + ur.URL + ")" + " | Not used");
                                break;
                            case UpdateAction.NONE:
                                break;
                        }
                    }
                    Report.AppendLine();
                }
            }
        }

        private bool AboveMaxUpdates()
        {
            if (MaxUpdates <= 0)
                return true;

            IEnumerable<UpdateReport> updates = Updates.SelectMany(kv => kv.Value).Where(u => u.Action != UpdateAction.NONE && u.Action != UpdateAction.FIGMA_MISSING);
            if (updates.Count() < MaxUpdates)
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
