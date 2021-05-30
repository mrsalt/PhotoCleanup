using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PhotoBackupCleanup
{
    internal class FileData
    {
        public FileData(FileInfo info)
        {
            fileInfo = info;
            key = info.Name + ":" + info.Length;
        }
        public FileInfo fileInfo;
        public string key;
        public string md5Hash;
        public bool corruptImage;

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
