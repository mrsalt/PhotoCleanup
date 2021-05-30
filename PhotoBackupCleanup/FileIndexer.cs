using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

        public static Dictionary<string, FileData> GetFiles(DirectoryInfo sourceDirectory, out Collection<FileData> duplicates)
        {
            fileHashPath = Path.Combine(sourceDirectory.FullName, "FileHashes.xml");
            duplicates = new Collection<FileData>();
            Collection<FileInfo> fileList = new Collection<FileInfo>();
            ReadFileHashes();
            FindFiles(sourceDirectory, fileList);
            CalculateImageHashes(fileList);
            WriteFileHashes();
            return FindDuplicates(duplicates);
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

        private static void FindFiles(DirectoryInfo directory, Collection<FileInfo> files)// Dictionary<string, FileData> files, Collection<FileData> duplicates)
        {
            foreach (FileInfo file in directory.EnumerateFiles())
            {
                if (file.Extension.Equals(".db"))
                    continue;
                files.Add(file);
            }
            foreach (DirectoryInfo dir in directory.EnumerateDirectories())
            {
                FindFiles(dir, files);
            }
        }

        private static void CalculateImageHash()
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
                    countHashed++;
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
                    //if (!Utilities.htmlOutput)
                    //    Console.Write("{0} files hashed.  {1:p1} complete.\r", processed, (double)processed / (double)sTotalFilesToProcess);
                }
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

        private static void CalculateImageHashes(Collection<FileInfo> files)
        {
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(OnUnhandledException);

            Stopwatch watch = new Stopwatch();
            watch.Start();
            countHashed = 0;
            toProcess = files;
            fileData = new Collection<FileData>();

            threadpool = new Thread[4];
            for (int i = 0; i < threadpool.Length; i++)
            {
                threadpool[i] = new Thread(CalculateImageHash);
                threadpool[i].Start();
            }
            // wait for all workers to finish:
            for (int i = 0; i < threadpool.Length; i++)
            {
                threadpool[i].Join();
            }

            watch.Stop();
            currentDomain.UnhandledException -= new UnhandledExceptionEventHandler(OnUnhandledException);
            Console.WriteLine("{0} files hashed in {1}", countHashed, watch.Elapsed);
        }

        private static Dictionary<string, FileData> FindDuplicates(Collection<FileData> duplicates)
        {
            Dictionary<string, FileData> result = new Dictionary<string, FileData>();
            foreach (FileData file in fileData)
            {
                if (file.corruptImage)
                {
                    Console.WriteLine("{0} is corrupt.", Utilities.FormatFileName(file.fileInfo.FullName));
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
