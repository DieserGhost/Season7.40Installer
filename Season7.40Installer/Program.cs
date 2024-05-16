using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace FNINSTALLER
{
    class Program
    {
        const string BASE_URL = "https://manifest.fnbuilds.services";
        const int CHUNK_SIZE = 67108864;

        public static HttpClient httpClient = new HttpClient();

        class ChunkedFile
        {
            public List<int> ChunksIds = new();
            public string File = string.Empty;
            public long FileSize = 0;
        }

        class ManifestFile
        {
            public string Name = string.Empty;
            public List<ChunkedFile> Chunks = new();
            public long Size = 0;
        }

        static string FormatBytesWithSuffix(long bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }

            return string.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
        }

        static async Task Download(ManifestFile manifest, string version, string resultPath)
        {
            long totalBytes = manifest.Size;
            long completedBytes = 0;
            int progressLength = 0;

            if (!Directory.Exists(resultPath))
                Directory.CreateDirectory(resultPath);

            SemaphoreSlim semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

            await Task.WhenAll(manifest.Chunks.Select(async chunkedFile =>
            {
                await semaphore.WaitAsync();

                try
                {
                    WebClient webClient = new WebClient();

                    string outputFilePath = Path.Combine(resultPath, chunkedFile.File);
                    var fileInfo = new FileInfo(outputFilePath);

                    if (File.Exists(outputFilePath) && fileInfo.Length == chunkedFile.FileSize)
                    {
                        completedBytes += chunkedFile.FileSize;
                        semaphore.Release();
                        return;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath));

                    using (FileStream outputStream = File.OpenWrite(outputFilePath))
                    {
                        foreach (int chunkId in chunkedFile.ChunksIds)
                        {
                        retry:

                            try
                            {
                                string chunkUrl = BASE_URL + $"/{version}/" + chunkId + ".chunk";
                                var chunkData = await webClient.DownloadDataTaskAsync(chunkUrl);

                                byte[] chunkDecompData = new byte[CHUNK_SIZE + 1];
                                int bytesRead;
                                long chunkCompletedBytes = 0;

                                MemoryStream memoryStream = new MemoryStream(chunkData);
                                GZipStream decompressionStream = new GZipStream(memoryStream, CompressionMode.Decompress);

                                while ((bytesRead = await decompressionStream.ReadAsync(chunkDecompData, 0, chunkDecompData.Length)) > 0)
                                {
                                    await outputStream.WriteAsync(chunkDecompData, 0, bytesRead);
                                    Interlocked.Add(ref completedBytes, bytesRead);
                                    Interlocked.Add(ref chunkCompletedBytes, bytesRead);

                                    double progress = (double)completedBytes / totalBytes * 100;
                                    string progressMessage = $"\rDownloaded: {FormatBytesWithSuffix(completedBytes)} / {FormatBytesWithSuffix(totalBytes)} ({progress:F2}%)";

                                    int padding = progressLength - progressMessage.Length;
                                    if (padding > 0)
                                        progressMessage += new string(' ', padding);

                                    Console.Write(progressMessage);
                                    progressLength = progressMessage.Length;
                                }

                                memoryStream.Close();
                                decompressionStream.Close();
                            }
                            catch (Exception ex)
                            {
                                goto retry;
                            }
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }));

            Console.WriteLine("\n\nFinished Downloading.\nPress any key to exit!");
            Thread.Sleep(100);
            Console.ReadKey();
        }

        static async Task<ManifestFile> GetManifestAsync(string version)
        {
            var manifestUrl = $"{BASE_URL}/{version}/{version}.manifest";
            var manifestResponse = await httpClient.GetStringAsync(manifestUrl);

            if (string.IsNullOrEmpty(manifestResponse))
            {
                throw new Exception("Failed to get manifest");
            }

            var manifest = JsonConvert.DeserializeObject<ManifestFile>(manifestResponse);

            if (manifest == null)
            {
                throw new Exception("Failed to parse manifest");
            }

            return manifest;
        }

        static async Task Main(string[] args)
        {
            Console.Clear();
            Console.Title = "Season7.40 Installer";
            Console.Write("\n\nFortnite Season Installer made by Ghost143\n\n");
            Console.Write("\n\n------------------------------------------\n\n");
            Console.Write("\n\nOriginal made by Ender & blk (EASYINSTALLER V2)\n\n");
            Console.Write("\n\n------------------------------------------\n\n");
            Console.Write("\n\nVersion: 7.40 (FN)\n\n");
            Console.Write("\n\n------------------------------------------\n\n");


            var targetVersion = "7.40"; // Version eg. 7.40
            var manifest = await GetManifestAsync(targetVersion);

            Console.Write("Please enter a game folder location: ");
            var targetPath = Console.ReadLine();
            Console.Write("\n");

            if (string.IsNullOrEmpty(targetPath))
            {
                await Main(args);
                return;
            }

            await Download(manifest, targetVersion, targetPath);
        }
    }
}
