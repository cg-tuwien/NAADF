using Microsoft.Xna.Framework;
using SharpDX.MediaFoundation.DirectX;
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
        public static void WriteNullTerminated(this Stream stream, string text)
        {
            byte[] buf = text.GetBytesFromString();
            stream.Write(buf, 0, buf.Length);
        }

        public static void WriteGuid(this Stream stream, Guid uuid)
        {
            byte[] buf = uuid.ToByteArray();
            stream.Write(buf, 0, buf.Length);
        }

        public static void WriteInt(this Stream stream, int data)
        {
            byte[] buf = BitConverter.GetBytes(data);
            stream.Write(buf, 0, buf.Length);
        }

        public static void WriteUInt(this Stream stream, uint data)
        {
            byte[] buf = BitConverter.GetBytes(data);
            stream.Write(buf, 0, buf.Length);
        }

        public static void WriteFloat(this Stream stream, float data)
        {
            byte[] buf = BitConverter.GetBytes(data);
            stream.Write(buf, 0, buf.Length);
        }

        public static void WriteVector3(this Stream stream, Vector3 data)
        {
            WriteFloat(stream, data.X);
            WriteFloat(stream, data.Y);
            WriteFloat(stream, data.Z);
        }

        public static void WriteDouble(this Stream stream, double data)
        {
            byte[] buf = BitConverter.GetBytes(data);
            stream.Write(buf, 0, buf.Length);
        }

        public static void WriteBool(this Stream stream, bool data)
        {
            stream.WriteByte((byte)(data ? 1 : 0));
        }

        public static void WriteShort(this Stream stream, short data)
        {
            byte[] buf = BitConverter.GetBytes(data);
            stream.Write(buf, 0, buf.Length);
        }

    }
}
