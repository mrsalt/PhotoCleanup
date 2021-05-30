using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

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
                                                                 ".psd", ".3gp", ".mp4" };
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
                return "<a href=\"file:///" + p + "\">" + p + "</a>";
            return p;
        }
    }
}
