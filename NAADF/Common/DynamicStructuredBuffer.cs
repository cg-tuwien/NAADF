using Microsoft.Xna.Framework.Graphics;
using SharpDX.Direct3D9;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.Common
{
    public class DynamicStructuredBuffer
    {
        private StructuredBuffer buffer;
        private GraphicsDevice device;
        private Type type;

        public DynamicStructuredBuffer(GraphicsDevice graphicsDevice, Type structureType, int elementCount, BufferUsage bufferUsage, ShaderAccess shaderAccess, StructuredBufferType bufferType = StructuredBufferType.Basic, int counterResetValue = -1, bool isDynamic = false)
        {
            this.device = graphicsDevice;
            this.type = structureType;
            buffer = new StructuredBuffer(graphicsDevice, structureType, elementCount, bufferUsage, shaderAccess, bufferType, counterResetValue, isDynamic);
        }

        public unsafe void SetNewMinCount(int elementCount, double increaseFac)
        {
            if (elementCount > buffer.ElementCount)
            {
                long newSize = (long)((double)elementCount * increaseFac);
                long newSizeInBytes = newSize * Marshal.SizeOf(type);
                if (newSizeInBytes > 0xFFFF0000)
                    newSize = 0xFFFF0000 / Marshal.SizeOf(type);

                Resize((int)newSize);
            }
        }

        public unsafe void Resize(int newElementCount)
        {
            if (newElementCount <= buffer.ElementCount)
                return;
            StructuredBuffer newBuffer = new StructuredBuffer(device, type, (int)newElementCount, buffer.BufferUsage, buffer.ShaderAccess, buffer.StructuredBufferType, buffer.CounterResetValue);
            if (Marshal.SizeOf(type) == 4)
                App.helper.CopyIntoStructuredBufferLarge(newBuffer, buffer, buffer.ElementCount);
            else
                buffer.CopyData(newBuffer, buffer.ElementCount * Marshal.SizeOf(type), 0, 0);
            buffer.Dispose();
            buffer = newBuffer;
        }

        public StructuredBuffer GetBuffer() { return buffer; }

        public void GetData<T>(T[] data, int startIndex, int elementCount) where T : struct
        {
            buffer.GetData(0, data, startIndex, elementCount);
        }

        public void GetData<T>(T[] data, int startIndex, int elementCount, int offset) where T : struct
        {
            buffer.GetData(offset, data, startIndex, elementCount);
        }

        public void SetData<T>(T[] data, int startIndex, int elementCount) where T : struct
        {
            buffer.SetData(data, startIndex, elementCount);
        }
    }
}
