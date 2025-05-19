using System;
using System.IO;

namespace PhotoBackupCleanup
{
    internal class FileData
    {
        public FileData(FileInfo info, bool isProtected)
        {
            fileInfo = info;
            key = info.Name + ":" + info.Length;
            this.isProtected = isProtected;
        }

        public FileInfo fileInfo;
        public string key;
        public string md5Hash;
        public bool corruptImage;
        public bool isProtected;

        internal void CalculateImageHash()
        {
            if (Utilities.IsImageFile(fileInfo))
            {
                try
                {
                    md5Hash = Utilities.CalculateImageMD5(fileInfo.FullName);
                    key = md5Hash; // this is a better hash than file name or size.
                }
                catch (System.ArgumentException)
                {
                    // image is corrupt
                    corruptImage = true;
                }
            }
        }

        internal void UpdateFromDataHash(DataHash dataHash)
        {
            if (!string.IsNullOrEmpty(dataHash.hash))
            {
                key = md5Hash = dataHash.hash;
            }
            else
            {
                corruptImage = true;
            }
        }
    }

    internal struct DataHash
    {
        public string hash;
        public DateTime lastModificationTime;
    }
}
