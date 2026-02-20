using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.Common.Extensions
{
    public static partial class ExtFile
    {
        public static byte[] GetBytesFromString(this string str)
        {
            byte[] bytes = new byte[str.Length + 1];
            Buffer.BlockCopy(Encoding.ASCII.GetBytes(str), 0, bytes, 0, bytes.Length - 1);
            bytes[^1] = 0;
            return bytes;
        }

        public static string ValidatePath(string path, out string fullPath)
        {
            fullPath = null;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception e)
            {
                if (e is PathTooLongException)
                    return "File path is too long!";
                else if (e is NotSupportedException || e is ArgumentException)
                    return "File path is not valid!";
                else
                    return "File path is not valid!";
            }
            return null;
        }
    }
}
