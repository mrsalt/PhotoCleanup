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
        private static Dictionary<string, DataHash> fileHashes;
        private static string fileHashPath;
        private static Thread[] threadpool;
        private static Collection<FileInfo> toProcess;
        private static Collection<FileData> fileData;
        private static int countHashed;
        private static int threadsDone = 0;

        public static Dictionary<string, FileData> GetFiles(TextWriter reportWriter, TextWriter progressWriter, List<DirectoryInfo> sourceDirectories, out Collection<FileData> duplicates)
        {
            fileData = new Collection<FileData>();
            foreach (DirectoryInfo sourceDirectory in sourceDirectories)
            {
                progressWriter.WriteLine("Searching for image files in {0}...", sourceDirectory.FullName);
                fileHashPath = Path.Combine(sourceDirectory.FullName, "FileHashes.xml");
                ReadFileHashes();
                Collection<FileInfo> fileList = new Collection<FileInfo>();
                FindFiles(progressWriter, sourceDirectory, fileList, 0);
                CalculateImageHashes(progressWriter, fileList);
                WriteFileHashes();
            }
            duplicates = new Collection<FileData>();
            return FindDuplicates(reportWriter, duplicates);
        }

        private static void ReadFileHashes()
        {
            fileHashes = new Dictionary<string, DataHash>(StringComparer.OrdinalIgnoreCase);
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
                            fileHashes.Add(reader.GetAttribute("path"), d);
                        }
                    }
                }
            }
        }

        private static void WriteFileHashes()
        {
            if (countHashed == 0)
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

        private static void FindFiles(TextWriter progressWriter, DirectoryInfo directory, Collection<FileInfo> files, int depth)// Dictionary<string, FileData> files, Collection<FileData> duplicates)
        {
            progressWriter.WriteLine("{0}{1}", padding((depth + 1) * 2), directory);
            try
            {
                foreach (FileInfo file in directory.EnumerateFiles())
                {
                    if (file.Extension.Equals(".db"))
                        continue;
                    files.Add(file);
                }
                foreach (DirectoryInfo dir in directory.EnumerateDirectories())
                {
                    if (dir.Name == ".tmp.drivedownload")
                        continue;
                    FindFiles(progressWriter, dir, files, depth + 1);
                }
            }
            catch (UnauthorizedAccessException uae)
            {
                System.Console.WriteLine(uae.Message);
            }
        }

        private static void CalculateImageHash()
        {
            try
            {
                while (true)
                {
                    FileInfo file;
                    lock (toProcess)
                    {
                        if (toProcess.Count == 0)
                            break;
                        file = toProcess[0];
                        toProcess.RemoveAt(0);
                    }

                    FileData f = new FileData(file);

                    bool found = false;
                    lock (fileHashes)
                    {
                        if (fileHashes.ContainsKey(file.FullName) && fileHashes[file.FullName].lastModificationTime == file.LastWriteTimeUtc)
                        {
                            found = true;
                            f.UpdateFromDataHash(fileHashes[file.FullName]);
                        }
                    }

                    if (!found && Utilities.IsImageFile(file))
                    {
                        f.CalculateImageHash();
                        Interlocked.Increment(ref countHashed);
                        DataHash d = new DataHash();
                        d.hash = f.md5Hash;
                        d.lastModificationTime = file.LastWriteTimeUtc;

                        lock (fileHashes)
                        {
                            if (fileHashes.ContainsKey(file.FullName))
                                fileHashes[file.FullName] = d;
                            else
                                fileHashes.Add(file.FullName, d);
                        }
                    }

                    lock (fileData)
                    {
                        fileData.Add(f);
                    }
                }
            }
            finally
            {
                Interlocked.Increment(ref threadsDone);
            }
        }

        static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
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

        private static void CalculateImageHashes(TextWriter progressWriter, Collection<FileInfo> files)
        {
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(OnUnhandledException);

            Stopwatch watch = new Stopwatch();
            watch.Start();
            countHashed = 0;
            toProcess = files;
            int totalToHash = files.Count;

            threadpool = new Thread[4];
            for (int i = 0; i < threadpool.Length; i++)
            {
                threadpool[i] = new Thread(CalculateImageHash);
                threadpool[i].Start();
            }

            // wait for all workers to finish:
            while (threadsDone < threadpool.Length)
            {
                Thread.Sleep(1000);
                progressWriter.Write("{0} files hashed.  {1:p1} complete.   \r", countHashed, (double)countHashed / (double)totalToHash);
            }

            watch.Stop();
            currentDomain.UnhandledException -= new UnhandledExceptionEventHandler(OnUnhandledException);
            progressWriter.WriteLine("{0} files hashed in {1}", countHashed, watch.Elapsed);
        }

        private static Dictionary<string, FileData> FindDuplicates(TextWriter reportWriter, Collection<FileData> duplicates)
        {
            Dictionary<string, FileData> result = new Dictionary<string, FileData>();
            foreach (FileData file in fileData)
            {
                if (file.corruptImage)
                {
                    reportWriter.WriteLine("{0} is corrupt.", Utilities.FormatFileName(file.fileInfo.FullName));
                    continue;
                }
                if (result.ContainsKey(file.key))
                    duplicates.Add(file);
                else
                    result.Add(file.key, file);
            }
            return result;
        }
    }
}
