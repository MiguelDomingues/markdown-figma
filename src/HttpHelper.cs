using System;
using System.Reflection;
using System.Diagnostics;
using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;
using Serilog;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net;
using System.ComponentModel;

namespace MarkdownFigma
{
    internal class HttpHelper
    {
        internal static byte[] HttpGet(string url, Dictionary<string, string> headers, int retries = 3)
        {
            Log.Debug("Downloading {URL}", url);
            try
            {
                byte[] data = TaskHTTPRequest(HttpMethod.Get, url, "", headers);
                if (data == null)
                    throw new Exception("No data has been received.");
                Log.Debug("Received {Bytes} bytes.", data.Length);
                return data;
            }
            catch (Exception e)
            {
                if (retries > 0)
                {
                    Log.Warning("Request failed with {Error}. Retrying...", e.Message);
                    return HttpGet(url, headers, retries - 1);
                }
                else
                    throw;
            }
        }

        private static void client_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            double bytesIn = double.Parse(e.BytesReceived.ToString());
            double totalBytes = double.Parse(e.TotalBytesToReceive.ToString());
            double percentage = bytesIn / totalBytes * 100;

            Log.Information(percentage.ToString());
        }

        internal static byte[] TaskHTTPRequest(HttpMethod method, string url, string content, Dictionary<string, string> headers)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            using var httpRequestMessage = new HttpRequestMessage
            {
                Method = method,
                RequestUri = new Uri(url),
                Content = content != null ? new StringContent(content) : null
            };

            if (headers != null)
                foreach (KeyValuePair<string, string> h in headers)
                    httpRequestMessage.Headers.Add(h.Key, h.Value);

            var result = client.SendAsync(httpRequestMessage).GetAwaiter().GetResult();
            if (result.IsSuccessStatusCode)
                return result.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

            return null;
        }

    }
}