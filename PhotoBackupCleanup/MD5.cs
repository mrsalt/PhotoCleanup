using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PhotoBackupCleanup
{
    class MD5
    {
        /*
        typedef struct {
	        MD5_u32plus lo, hi;
	        MD5_u32plus a, b, c, d;
	        unsigned char buffer[64];
	        MD5_u32plus block[16];
        } MD5_CTX;*/

        // See http://stackoverflow.com/questions/10320502/c-sharp-calling-c-function-that-returns-struct-with-fixed-size-char-array
        [StructLayout(LayoutKind.Sequential, Size = 152)] // 6 * 4 + 64 + 16 * 4
        struct MD5_CTX
        {
        }

        [DllImport("MD5.dll", CallingConvention=CallingConvention.Cdecl)]
        private static extern void MD5_Init(ref MD5_CTX ctx);

        [DllImport("MD5.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void MD5_Update(ref MD5_CTX ctx, IntPtr data, UInt32 size);

        [DllImport("MD5.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe void MD5_Final(byte * result, ref MD5_CTX ctx);

        MD5_CTX ctx;

        public MD5()
        {
        }

        public void Update(IntPtr data, UInt32 size)
        {
            MD5_Update(ref ctx, data, size);
        }

        public string Final()
        {
            unsafe
            {
                byte* result = stackalloc byte[16];
                MD5_Final(result, ref ctx);
                StringBuilder sb = new StringBuilder(32);
                for (int i = 0; i < 16; i++)
                    sb.AppendFormat("{0:x2}", (int) result[i]);
                return sb.ToString();
            }
        }

    }
}
