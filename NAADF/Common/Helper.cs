using Microsoft.Xna.Framework.Graphics;
using NAADF;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.Common
{
    public class Helper
    {
        Effect dataCopyEffect;

        public Helper()
        {
            dataCopyEffect = App.contentManager.Load<Effect>("shaders/dataCopy");
        }

        public void CopyIntoStructuredBufferLarge(StructuredBuffer dst, uint[] src, int count)
        {
            if (count < 500000000)
                dst.SetData(src, 0, count);
            else
            {
                uint[] srcTemp = new uint[100000000];
                StructuredBuffer tempData = new StructuredBuffer(App.graphicsDevice, typeof(uint), 100000000, BufferUsage.None, ShaderAccess.ReadWrite);
                int remainingCount = count;
                int curOffset = 0;
                while (remainingCount > 0)
                {
                    int curCount = Math.Min(remainingCount, 100000000);
                    for (int i = 0; i < curCount; i++)
                    {
                        srcTemp[i] = src[i + curOffset];
                    }
                    tempData.SetData(srcTemp, 0, curCount);

                    dataCopyEffect.Parameters["offsetSrc"].SetValue(0);
                    dataCopyEffect.Parameters["offsetDst"].SetValue(curOffset);
                    dataCopyEffect.Parameters["count"].SetValue(curCount);
                    dataCopyEffect.Parameters["srcData"].SetValue(tempData);
                    dataCopyEffect.Parameters["dstData"].SetValue(dst);
                    dataCopyEffect.Techniques[0].Passes["CopyData"].ApplyCompute();
                    App.graphicsDevice.DispatchCompute((curCount + 63) / 64, 1, 1);

                    remainingCount -= curCount;
                    curOffset += curCount;
                }
                tempData.Dispose();
            }
        }

        public void CopyIntoStructuredBufferLarge(StructuredBuffer dst, StructuredBuffer src, int count)
        {
            if (count < 250000000)
                src.CopyData(dst, count * 4, 0, 0);
            else
            {
                int remainingCount = count;
                int curOffset = 0;
                while (remainingCount > 0)
                {
                    int curCount = Math.Min(remainingCount, 100000000);

                    dataCopyEffect.Parameters["offsetSrc"].SetValue(curOffset);
                    dataCopyEffect.Parameters["offsetDst"].SetValue(curOffset);
                    dataCopyEffect.Parameters["count"].SetValue(curCount);
                    dataCopyEffect.Parameters["srcData"].SetValue(src);
                    dataCopyEffect.Parameters["dstData"].SetValue(dst);
                    dataCopyEffect.Techniques[0].Passes["CopyData"].ApplyCompute();
                    App.graphicsDevice.DispatchCompute((curCount + 63) / 64, 1, 1);

                    remainingCount -= curCount;
                    curOffset += curCount;
                }
            }
        }

        public void CopyFromStructuredBufferLarge(uint[] dst, StructuredBuffer src, int count)
        {
            if (count < 500000000)
                src.GetData(dst, 0, count);
            else
            {
                uint[] dstTemp = new uint[100000000];
                StructuredBuffer tempData = new StructuredBuffer(App.graphicsDevice, typeof(uint), 100000000, BufferUsage.None, ShaderAccess.ReadWrite);
                int remainingCount = count;
                int curOffset = 0;
                while (remainingCount > 0)
                {
                    int curCount = Math.Min(remainingCount, 100000000);

                    dataCopyEffect.Parameters["offsetSrc"].SetValue(curOffset);
                    dataCopyEffect.Parameters["offsetDst"].SetValue(0);
                    dataCopyEffect.Parameters["count"].SetValue(curCount);
                    dataCopyEffect.Parameters["srcData"].SetValue(src);
                    dataCopyEffect.Parameters["dstData"].SetValue(tempData);
                    dataCopyEffect.Techniques[0].Passes["CopyData"].ApplyCompute();
                    App.graphicsDevice.DispatchCompute((curCount + 63) / 64, 1, 1);

                    tempData.GetData(dstTemp, 0, curCount);
                    for (int i = 0; i < curCount; i++)
                    {
                        dst[curOffset + i] = dstTemp[i];
                    }

                    remainingCount -= curCount;
                    curOffset += curCount;
                }
                tempData.Dispose();
            }
        }
    }
}
