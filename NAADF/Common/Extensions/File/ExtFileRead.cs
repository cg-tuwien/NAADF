using Microsoft.Xna.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.Common.Extensions
{
    public static partial class ExtFile
    {
        private static byte[] buffer = new byte[16];

        public static string ReadNullTerminated(this Stream stream)
        {
            var sb = new StringBuilder();
            int nc;
            while ((nc = stream.ReadByte()) > 0)
                sb.Append((char)nc);

            return sb.ToString();
        }

        public static Guid ReadGuid(this Stream stream)
        {
            stream.Read(buffer, 0, 16);
            return new Guid(buffer);
        }

        public static int ReadInt(this Stream stream)
        {
            stream.Read(buffer, 0, 4);
            return BitConverter.ToInt32(buffer, 0);
        }

        public static uint ReadUInt(this Stream stream)
        {
            stream.Read(buffer, 0, 4);
            return BitConverter.ToUInt32(buffer, 0);
        }

        public static float ReadFloat(this Stream stream)
        {
            stream.Read(buffer, 0, 4);
            return BitConverter.ToSingle(buffer, 0);
        }

        public static Vector3 ReadVector3(this Stream stream)
        {
            Vector3 data;
            data.X = ReadFloat(stream);
            data.Y = ReadFloat(stream);
            data.Z = ReadFloat(stream);
            return data;
        }

        public static double ReadDouble(this Stream stream)
        {
            stream.Read(buffer, 0, 8);
            return BitConverter.ToDouble(buffer, 0);
        }

        public static bool ReadBool(this Stream stream)
        {
            return stream.ReadByte() > 0;
        }

        public static short ReadShort(this Stream stream)
        {
            stream.Read(buffer, 0, 2);
            return BitConverter.ToInt16(buffer, 0);
        }
    }
}
