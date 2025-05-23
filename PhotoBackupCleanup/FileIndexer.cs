using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Xml;

namespace PhotoBackupCleanup
{
    class FileIndexer
    {
        private static String fileHashesFileName = "FileHashes.xml";
        private Dictionary<string, DataHash> fileHashesRead;
        private Dictionary<string, DataHash> fileHashes;
        private string fileHashPath;
        private Thread[] threadpool;
        private Collection<FileData> toProcess;
        private Collection<FileData> fileData;
        private int countHashed;
        private int countProcessed;
        private int threadsDone;
        private TextWriter reportWriter;
        private TextWriter progressWriter;

        public FileIndexer(TextWriter reportWriter, TextWriter progressWriter)
        {
            this.reportWriter = reportWriter;
            this.progressWriter = progressWriter;
        }

        public Collection<FileData> GetFiles(DirectoryInfo sourceDirectory)
        {
            fileData = new Collection<FileData>();
            progressWriter.WriteLine("Searching for image files in {0}...", sourceDirectory.FullName);
            fileHashPath = Path.Combine(sourceDirectory.FullName, fileHashesFileName);
            ReadFileHashes();
            progressWriter.WriteLine("Found {0:N0} files in FileHashes.xml", fileHashesRead.Count);
            Collection<FileData> fileList = new Collection<FileData>();
            FindFiles(sourceDirectory, fileList, false, 0);
            CalculateImageHashes(fileList);
            WriteFileHashes();
            return fileData;
        }

        private void ReadFileHashes()
        {
            fileHashesRead = new Dictionary<string, DataHash>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(fileHashPath))
                return;
            using (XmlReader reader = XmlReader.Create(fileHashPath))
            {
                while (reader.Read())
                {
                    if (reader.IsStartElement())
                    {
                        if (reader.Name == "file" && reader.HasAttributes)
                        {
                            DataHash d = new DataHash();
                            d.hash = reader.GetAttribute("hash");
                            d.lastModificationTime = DateTime.Parse(reader.GetAttribute("date"), null, System.Globalization.DateTimeStyles.RoundtripKind);
                            reader.GetAttribute("path");
                            fileHashesRead.Add(reader.GetAttribute("path"), d);
                        }
                    }
                }
            }
        }

        private void WriteFileHashes()
        {
            if (countHashed == 0 && fileHashes.Count == fileHashesRead.Count)
                return;
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.IndentChars = "\t";
            using (XmlWriter writer = XmlWriter.Create(fileHashPath, settings))
            {
                writer.WriteStartElement("files");
                foreach (KeyValuePair<string, DataHash> pair in fileHashes)
                {
                    writer.WriteStartElement("file");
                    writer.WriteAttributeString("path", pair.Key);
                    writer.WriteAttributeString("hash", pair.Value.hash);
                    // how to round-trip a datetime value in .net:
                    // https://msdn.microsoft.com/en-us/library/bb882584%28v=vs.110%29.aspx
                    writer.WriteAttributeString("date", pair.Value.lastModificationTime.ToString("o"));
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
        }

        private static String padding(int spaces)
        {
            return new String(' ', spaces);
        }

        private void FindFiles(DirectoryInfo directory, Collection<FileData> files, bool protectedFiles, int depth)
        {
            try
            {
                Collection<FileInfo> fileInfos = new Collection<FileInfo>();
                foreach (FileInfo file in directory.EnumerateFiles())
                {
                    if (file.Extension.Equals(".db") || file.Name.Equals(fileHashesFileName))
                    {
                        continue;
                    }
                    if (file.Name == ".ignore duplicates")
                    {
                        protectedFiles = true;
                        progressWriter.WriteLine("Found \".ignore duplicates\".  Will not delete duplicate files in {0}", directory.FullName);
                    }
                    else
                    {
                        fileInfos.Add(file);
                    }
                }
                long size = 0;
                foreach (FileInfo file in fileInfos)
                {
                    files.Add(new FileData(file, protectedFiles));
                    size += file.Length;
                }
                progressWriter.WriteLine("{0}{1} ({2:N0} files, {3})", padding((depth + 1) * 2), directory, fileInfos.Count, Utilities.ByteSuffix(size));
                foreach (DirectoryInfo dir in directory.EnumerateDirectories())
                {
                    if (dir.Name == ".tmp.drivedownload")
                        continue;
                    FindFiles(dir, files, protectedFiles, depth + 1);
                }
            }
            catch (UnauthorizedAccessException uae)
            {
                progressWriter.WriteLine(uae.Message);
            }
        }

        private void CalculateImageHash()
        {
            try
            {
                while (true)
                {
                    FileData file;
                    lock (toProcess)
                    {
                        if (toProcess.Count == 0)
                            break;
                        file = toProcess[0];
                        toProcess.RemoveAt(0);
                    }

                    bool found = false;
                    lock (fileHashesRead)
                    {
                        DataHash dataHash;
                        if (fileHashesRead.TryGetValue(file.fileInfo.FullName, out dataHash) && dataHash.lastModificationTime == file.fileInfo.LastWriteTimeUtc)
                        {
                            found = true;
                            file.UpdateFromDataHash(dataHash);
                            lock (fileHashes)
                            {
                                fileHashes.Add(file.fileInfo.FullName, dataHash);
                            }
                        }
                    }

                    if (!found && Utilities.IsImageFile(file.fileInfo))
                    {
                        file.CalculateImageHash();
                        Interlocked.Increment(ref countHashed);
                        DataHash dataHash = new DataHash();
                        dataHash.hash = file.md5Hash;
                        dataHash.lastModificationTime = file.fileInfo.LastWriteTimeUtc;

                        lock (fileHashes)
                        {
                            fileHashes.Add(file.fileInfo.FullName, dataHash);
                        }
                    }

                    lock (fileData)
                    {
                        countProcessed++;
                        fileData.Add(file);
                    }
                }
            }
            finally
            {
                Interlocked.Increment(ref threadsDone);
            }
        }

        void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;
            Console.WriteLine("MyHandler caught : " + e.Message);
            Console.WriteLine("Runtime terminating: {0}", args.IsTerminating);

            for (int i = 0; i < threadpool.Length; i++)
            {
                if (threadpool[i].IsAlive)
                    threadpool[i].Abort();
            }

            WriteFileHashes();
            Environment.Exit(-1);
        }

        private void CalculateImageHashes(Collection<FileData> files)
        {
            fileHashes = new Dictionary<string, DataHash>(StringComparer.OrdinalIgnoreCase);
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(OnUnhandledException);

            Stopwatch watch = new Stopwatch();
            watch.Start();
            countHashed = 0;
            countProcessed = 0;
            toProcess = files;
            int totalToHash = files.Count;

            threadpool = new Thread[4];
            threadsDone = 0;
            for (int i = 0; i < threadpool.Length; i++)
            {
                threadpool[i] = new Thread(CalculateImageHash);
                threadpool[i].Start();
            }

            // wait for all workers to finish:
            while (threadsDone < threadpool.Length)
            {
                Thread.Sleep(1000);
                progressWriter.Write("{0:N0} files decoded and hashed, {1:N0} files found in cache.  {2:p1} complete.   \r", countHashed, countProcessed - countHashed, (double)countProcessed / (double)totalToHash);
            }
            progressWriter.WriteLine();

            watch.Stop();
            currentDomain.UnhandledException -= new UnhandledExceptionEventHandler(OnUnhandledException);
            progressWriter.WriteLine("{0:N0} files processed in {1}", countProcessed, watch.Elapsed);
        }

        public void FindDuplicates(Collection<FileData> fileData, Dictionary<string, FileData> result, Collection<FileData> duplicates)
        {
            foreach (FileData file in fileData)
            {
                if (file.corruptImage)
                {
                    reportWriter.WriteLine("{0} ({1}) is corrupt.", Utilities.FormatFileName(file.fileInfo.FullName), Utilities.ByteSuffix(file.fileInfo.Length));
                    continue;
                }
                FileData existing;
                if (result.TryGetValue(file.key, out existing))
                {
                    if (!file.isProtected && !existing.isProtected)
                    {
                        duplicates.Add(file);
                    }
                }
                else
                {
                    result.Add(file.key, file);
                }
            }
        }
    }
}
