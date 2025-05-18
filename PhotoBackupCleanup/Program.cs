using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;

namespace PhotoBackupCleanup
{
    class Program
    {
        private static void PrintHelp()
        {
            Console.WriteLine("PhotoCleanup Utility");
            Console.WriteLine("  Reports on duplicate image files, deletes duplicates, copies missing files.");
            Console.WriteLine("  Synchronizes two sets of files.");
            Console.WriteLine();
            Console.WriteLine("  Required Arguments:");
            Console.WriteLine("  -s              Specify a source directory.  Searches this directory and all subfolders");
            Console.WriteLine("                  for image files.");
            Console.WriteLine("  Optional Arguments:");
            Console.WriteLine("  -d              Specify the destination directory.  If only source directories are");
            Console.WriteLine("                  specified, program will only search for (and optionally delete)");
            Console.WriteLine("                  duplicate files.");
            Console.WriteLine("  -delete         deletes duplicates in src, as along as dest is not specified.");
            Console.WriteLine("                  if dest is specified, deletes files in dest not found in src.");
            Console.WriteLine("                  before using this option, it is wise to run -reportMissing.");
            Console.WriteLine("  -copy           copies files found in src not found in dest.");
            Console.WriteLine("  -reportMissing  reports files found in dest not found in src.");
            Console.WriteLine("  -html           Output the report in html format.");
        }

        static SortedSet<String> filesToIgnore = new SortedSet<string>();

        static void Main(string[] args)
        {
            List<DirectoryInfo> sourceDirectories = new List<DirectoryInfo>();
            DirectoryInfo destDirectory = null;
            bool deleteFiles = false;
            bool copyMissingFiles = false;
            bool reportMissingFiles = false;

            filesToIgnore.Add("desktop.ini");
            filesToIgnore.Add("FileHashes.xml");

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-s":
                    {
                        // specify source directories only to find duplicates.
                        // program will search each source directory (and its subfolders) for
                        // all image files.
                        sourceDirectories.Add(new DirectoryInfo(args[++i]));
                        break;
                    }
                    case "-d":
                    {
                        destDirectory = new DirectoryInfo(args[++i]);
                        break;
                    }
                    case "-delete":
                    {
                        // deletes duplicates in src, as along as dest is not specified.
                        
                        // if dest is specified, deletes files in dest not found in src.
                        // before using this option, it is wise to run -reportMissing.
                        deleteFiles = true;
                        break;
                    }
                    case "-copy":
                    {
                        // copies files found in src not found in dest
                        copyMissingFiles = true;
                        break;
                    }
                    case "-reportMissing":
                    {
                        // reports files found in dest not found in src.
                        reportMissingFiles = true;
                        break;
                    }
                    case "-html":
                    {
                        Utilities.htmlOutput = true;
                        break;
                    }
                }
            }

            if (args.Length == 0 || sourceDirectories.Count == 0)
            {
                PrintHelp();
                Environment.Exit(-1);
                return;
            }

            Collection<FileData> sourceDuplicates;

            if (Utilities.htmlOutput)
                Console.WriteLine("<pre>");

            Dictionary<string, FileData> sourceFiles = FileIndexer.GetFiles(sourceDirectories, out sourceDuplicates);
            if (sourceFiles == null)
                return;

            if (destDirectory == null)
            {
                Console.WriteLine("{0} duplicates found in {1}: ", sourceDuplicates.Count, String.Join(", ", sourceDirectories));
                foreach (FileData dup in sourceDuplicates)
                {
                    //if (!string.IsNullOrEmpty(dup.md5Hash) && !string.IsNullOrEmpty(sourceFiles[dup.key].md5Hash) &&
                    //    dup.md5Hash != sourceFiles[dup.key].md5Hash)
                    //if (dup.fileInfo.Length != sourceFiles[dup.key].fileInfo.Length)
                    //    throw new Exception(String.Format("{0} is not a duplicate of {1}", Utilities.FormatFileName(dup.fileInfo.FullName), Utilities.FormatFileName(sourceFiles[dup.key].fileInfo.FullName)));
                    if (deleteFiles)
                    {
                        if (!Utilities.IsMediaFile(dup.fileInfo))
                        {
                            Console.WriteLine("Not deleting {0} (duplicate of {1}): not an image file", Utilities.FormatFileName(dup.fileInfo.FullName), Utilities.FormatFileName(sourceFiles[dup.key].fileInfo.FullName));
                        }
                        else
                        {
                            Console.WriteLine("Deleting {0} (duplicate of {1})", Utilities.FormatFileName(dup.fileInfo.FullName), Utilities.FormatFileName(sourceFiles[dup.key].fileInfo.FullName));
                            File.Delete(dup.fileInfo.FullName);
                        }
                    }
                    else
                    {
                        Console.WriteLine("{0} (duplicate of {1}){2}", Utilities.FormatFileName(dup.fileInfo.FullName), Utilities.FormatFileName(sourceFiles[dup.key].fileInfo.FullName), Utilities.IsMediaFile(dup.fileInfo) ? "" : " (not an image file)");
                    }
                }
            }
            else if (sourceFiles.Count > 0)
            {
                if (sourceDirectories.Count == 1)
                {
                    Collection<FileData> destDuplicates;
                    List<DirectoryInfo> destDirectoriesList = new List<DirectoryInfo>();
                    destDirectoriesList.Add(destDirectory);
                    Dictionary<string, FileData> destFiles = FileIndexer.GetFiles(destDirectoriesList, out destDuplicates);

                    if (reportMissingFiles)
                    {
                        ReportOnMissingSourceFiles(sourceDirectories[0], destDirectory, sourceFiles, destFiles, destDuplicates);
                    }
                    else
                    {
                        Console.WriteLine();
                        CopyMissingFiles(sourceDirectories[0], destDirectory, sourceFiles, copyMissingFiles);
                        Console.WriteLine();
                        DeleteDestFiles(sourceDirectories[0].FullName, destDirectory.FullName, destDirectory, deleteFiles);
                    }
                }
                else
                {
                    Console.WriteLine("Missing files report, copying missing files, and deleting destination files can only be done when a single source directory is provided.");
                }
            }

            if (Utilities.htmlOutput)
                Console.WriteLine("</pre>");
        }

        private static void CopyMissingFiles(DirectoryInfo sourceDirectory, DirectoryInfo destDirectory, Dictionary<string, FileData> sourceFiles, bool actuallyCopy)
        {
            foreach (KeyValuePair<string, FileData> pair in sourceFiles)
            {
                FileInfo srcInfo = pair.Value.fileInfo;
                string destPath = srcInfo.FullName.Replace(sourceDirectory.FullName, destDirectory.FullName);
                FileInfo destInfo = new FileInfo(destPath);
                if (!destInfo.Exists || destInfo.Length != srcInfo.Length)
                {
                    if (filesToIgnore.Contains(srcInfo.Name))
                    {
                        continue;
                    }
                    if (!Utilities.IsMediaFile(srcInfo))
                    {
                        Console.WriteLine("Warning - nonstandard file extension:");
                    }
                    if (actuallyCopy)
                    {
                        Console.WriteLine("Copying {0} to {1}", srcInfo.FullName, destPath);
                        DirectoryInfo destDir = new DirectoryInfo(new FileInfo(destPath).DirectoryName);
                        if (!destDir.Exists)
                        {
                            Directory.CreateDirectory(destDir.FullName);
                        }
                        File.Copy(srcInfo.FullName, destPath, true);
                    }
                    else
                    {
                        Console.WriteLine("Found file to copy: {0} to {1}", srcInfo.FullName, destPath);
                    }
                }
            }
        }

        private static void ReportOnMissingSourceFiles(DirectoryInfo sourceDirectory, DirectoryInfo destDirectory, Dictionary<string, FileData> sourceFiles, Dictionary<string, FileData> destFiles, Collection<FileData> destDuplicates)
        {
            Console.WriteLine("{0} contains {1} duplicates.", destDirectory.FullName, destDuplicates.Count);

            Dictionary<long, Collection<FileData>> sourceFilesBySize;
            Dictionary<string, Collection<FileData>> sourceFilesByName;
            BuildSizeBasedDictionary(sourceFiles, out sourceFilesBySize);
            BuildNameBasedDictionary(sourceFiles, out sourceFilesByName);

            int sourceMissing = 0;
            Collection<string> matches = new Collection<string>();
            Collection<FileData> missing = new Collection<FileData>();
            foreach (KeyValuePair<string, FileData> pair in destFiles)
            {
                if (!sourceFiles.ContainsKey(pair.Key))
                {
                    MatchType matchType = MatchType.Undefined;
                    double fileSizeDiffPercent = 0;
                    FileData match = MatchingFileFound(pair.Value, sourceFilesBySize, sourceFilesByName, ref matchType, ref fileSizeDiffPercent);
                    if (match != null && (matchType == MatchType.SizeAndBytes || fileSizeDiffPercent < 0.03))
                    {
                        if (matchType != MatchType.SizeAndBytes)
                            matches.Add(String.Format("{0} MATCHES {1} ({2}, file size diff: {3:p})", Utilities.FormatFileName(pair.Value.fileInfo.FullName), Utilities.FormatFileName(match.fileInfo.FullName), matchType, fileSizeDiffPercent));
                        else
                            matches.Add(String.Format("{0} MATCHES {1} ({2})", Utilities.FormatFileName(pair.Value.fileInfo.FullName), Utilities.FormatFileName(match.fileInfo.FullName), matchType));
                        continue;
                    }
                    missing.Add(pair.Value);
                    if (matchType == MatchType.Name)
                        Console.WriteLine("{0} not found under {1} ({2} file size diff: {3:p})", Utilities.FormatFileName(pair.Value.fileInfo.FullName), sourceDirectory.FullName, Utilities.FormatFileName(match.fileInfo.FullName), fileSizeDiffPercent);
                    else
                        Console.WriteLine("{0} not found under {1}", Utilities.FormatFileName(pair.Value.fileInfo.FullName), sourceDirectory.FullName);
                    sourceMissing++;
                }
            }
            Console.WriteLine("{0} files found under {1} not found under {2}.", sourceMissing, destDirectory.FullName, sourceDirectory.FullName);
            Console.WriteLine();

            Console.WriteLine("{0} files match a file of a different name (or same name but different size):", matches.Count);
            foreach (string match in matches)
            {
                Console.WriteLine(match);
            }
        }

        enum MatchType
        {
            Undefined,
            SizeAndBytes,
            NameAndSimilarSize,
            Name
        };

        private static FileData MatchingFileFound(FileData fileData, Dictionary<long, Collection<FileData>> sourceFilesBySize, Dictionary<string, Collection<FileData>> sourceFilesByName, ref MatchType matchType, ref double matchSimilarity)
        {
            if (sourceFilesBySize.ContainsKey(fileData.fileInfo.Length))
            {
                Collection<FileData> sameSizedFiles = sourceFilesBySize[fileData.fileInfo.Length];
                byte[] abytes = File.ReadAllBytes(fileData.fileInfo.FullName);
                foreach (FileData file in sameSizedFiles)
                {
                    if (FileContentsMatch(abytes, file))
                    {
                        matchType = MatchType.SizeAndBytes;
                        return file;
                    }
                }
            }
            if (sourceFilesByName.ContainsKey(fileData.fileInfo.Name))
            {
                Collection<FileData> sameNamedFiles = sourceFilesByName[fileData.fileInfo.Name];
                double bestDiff = double.MaxValue;
                FileData bestMatch = null;
                foreach (FileData file in sameNamedFiles)
                {
                    double percentDifferent = (double)Math.Abs(fileData.fileInfo.Length - file.fileInfo.Length) / (double)fileData.fileInfo.Length;
                    if (percentDifferent < bestDiff)
                    {
                        bestMatch = file;
                        bestDiff = percentDifferent;
                    }
                }
                if (bestMatch != null)
                {
                    matchSimilarity = bestDiff;
                    matchType = bestDiff < 0.01 ? MatchType.NameAndSimilarSize : MatchType.Name;
                    return bestMatch;
                }
            }
            return null;
        }

        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int memcmp(byte[] b1, byte[] b2, long count);

        static bool ByteArrayCompare(byte[] b1, byte[] b2)
        {
            // Validate buffers are the same length.
            // This also ensures that the count does not exceed the length of either buffer.  
            return b1.Length == b2.Length && memcmp(b1, b2, b1.Length) == 0;
        }

        private static bool FileContentsMatch(byte[] abytes, FileData b)
        {
            byte[] bbytes = File.ReadAllBytes(b.fileInfo.FullName);
            return ByteArrayCompare(abytes, bbytes);
        }

        private static void BuildSizeBasedDictionary(Dictionary<string, FileData> files, out Dictionary<long, Collection<FileData>> dictionary)
        {
            dictionary = new Dictionary<long, Collection<FileData>>();
            foreach (KeyValuePair<string, FileData> pair in files)
            {
                long size = pair.Value.fileInfo.Length;
                if (!dictionary.ContainsKey(size))
                {
                    dictionary.Add(size, new Collection<FileData>());
                }
                dictionary[size].Add(pair.Value);
            }
        }

        private static void BuildNameBasedDictionary(Dictionary<string, FileData> files, out Dictionary<string, Collection<FileData>> dictionary)
        {
            dictionary = new Dictionary<string, Collection<FileData>>();
            foreach (KeyValuePair<string, FileData> pair in files)
            {
                string name = pair.Value.fileInfo.Name;
                if (!dictionary.ContainsKey(name))
                {
                    dictionary.Add(name, new Collection<FileData>());
                }
                dictionary[name].Add(pair.Value);
            }
        }

        private static void DeleteDestFiles(string srcPath, string destPath, DirectoryInfo directory, bool actuallyDelete)
        {
            Collection<FileInfo> toDelete = new Collection<FileInfo>();
            foreach (FileInfo file in directory.EnumerateFiles())
            {
                string fullSrcPath = file.FullName.Replace(destPath, srcPath);
                if (!File.Exists(fullSrcPath))
                {
                    toDelete.Add(file);
                    if (actuallyDelete)
                        File.Delete(file.FullName);
                }
            }
            if (toDelete.Count > 5)
            {
                if (actuallyDelete)
                    Console.WriteLine("Deleting {0} files in {1}", toDelete.Count, directory.FullName);
                else
                    Console.WriteLine("Found {0} files in {1} to delete (not found in {2}).", toDelete.Count, directory.FullName, srcPath);
            }
            else
            {
                foreach (FileInfo file in toDelete)
                {
                    if (actuallyDelete)
                        Console.WriteLine("Deleting file {0}", file.FullName);
                    else
                        Console.WriteLine("Found file {0} to delete (not found in {1})", file.FullName, srcPath);

                }
            }
            foreach (DirectoryInfo dir in directory.EnumerateDirectories())
            {
                string fullSrcPath = dir.FullName.Replace(destPath, srcPath);
                if (!Directory.Exists(fullSrcPath))
                {
                    if (actuallyDelete)
                    {
                        Console.WriteLine("Deleting directory {0}", dir.FullName);
                        Directory.Delete(dir.FullName, true);
                    }
                    else
                    {
                        Console.WriteLine("Found directory to delete {0}", dir.FullName);
                    }
                }
                else
                {
                    DeleteDestFiles(srcPath, destPath, dir, actuallyDelete);
                }
            }
        }

    }
}
