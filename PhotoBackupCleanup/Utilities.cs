using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;

namespace PhotoBackupCleanup
{
    static class Utilities
    {
        public static bool htmlOutput = false;
        /*

        public static string ComputeSha1Hash(byte [] data)
        {
            SHA1 sha = new SHA1CryptoServiceProvider();

            byte[] hash = sha.ComputeHash(data);

            StringBuilder sBuilder = new StringBuilder();

            // Loop through each byte of the hashed data  
            // and format each one as a hexadecimal string. 
            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }

            // Return the hexadecimal string. 
            return sBuilder.ToString();
        }
        */

        public static string CalculateImageMD5(string filePath)
        {
            MD5 md5 = new MD5();

            //using (Image image = Image.FromFile(filePath))
            //{
                //using (Bitmap bitmap = new Bitmap(image))
                using (Bitmap bitmap = new Bitmap(filePath))
                {
                    Rectangle bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                    BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadWrite, bitmap.PixelFormat); // = bitmap.LockBits()
                    GC.AddMemoryPressure(data.Stride * data.Height);
                    for (int y = 0; y < data.Height; y++)
                    {
                        IntPtr rowPtr = data.Scan0 + data.Stride * y;
                        uint size = (uint)data.Stride;
                        md5.Update(rowPtr, size);
                    }
                    bitmap.UnlockBits(data);
                    //md5.TransformBlock()
                    return md5.Final();
                }
            //}
        }

        static string[] standardImageExtensions = { ".jpg", ".tif", ".png", ".bmp" };

        static string[] movieOrOtherImageRelatedExtensions = { ".avi", ".mpg", ".thm", //canon jpeg thumbnail - plus perhaps some proprietary stuff };
                                                                 ".psd", ".3gp", ".mp4", ".nef", ".xmp", ".dng" };
        public static bool IsMediaFile(FileInfo file)
        {
            string lowerExt = file.Extension.ToLower();
            foreach (string ext in standardImageExtensions)
            {
                if (ext == lowerExt)
                {
                    return true;
                }
            }
            foreach (string ext in movieOrOtherImageRelatedExtensions)
            {
                if (ext == lowerExt)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsImageFile(FileInfo file)
        {
            string lowerExt = file.Extension.ToLower();
            foreach (string ext in standardImageExtensions)
            {
                if (ext == lowerExt)
                {
                    return true;
                }
            }
            return false;
        }

        public static string FormatFileName(string p)
        {
            if (htmlOutput)
                return "<a href=\"" + FilePathToFileUrl(p) + "\">" + p + "</a>";
            return p;
        }

        // Kudos to Tommaso Ercole, https://stackoverflow.com/questions/1546419/convert-file-path-to-a-file-uri/74852300#74852300
        public static string FilePathToFileUrl(string path)
        {
            return new UriBuilder("file", string.Empty)
            {
                Path = path
                        .Replace("%", $"%{(int)'%':X2}")
                        .Replace("[", $"%{(int)'[':X2}")
                        .Replace("]", $"%{(int)']':X2}"),
            }
                .Uri
                .AbsoluteUri;
        }

        // Thank you JLRishe, https://stackoverflow.com/questions/14488796/does-net-provide-an-easy-way-convert-bytes-to-kb-mb-gb-etc
        static readonly string[] SizeSuffixes =
                  { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        public static string ByteSuffix(long value, int decimalPlaces = 1)
        {
            if (value < 0) { return "-" + ByteSuffix(-value, decimalPlaces); }

            int i = 0;
            decimal dValue = (decimal)value;
            while (Math.Round(dValue, decimalPlaces) >= 1000)
            {
                dValue /= 1024;
                i++;
            }

            return string.Format("{0:n" + decimalPlaces + "} {1}", dValue, SizeSuffixes[i]);
        }
    }
}
