using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.Common
{
    public struct Uint2
    {
        public uint data1;
        public uint data2;

        public Uint2()
        {

        }

        public Uint2(uint data1, uint data2)
        {
            this.data1 = data1;
            this.data2 = data2;
        }
    }

    public struct Uint3
    {
        uint data1;
        uint data2;
        uint data3;
    }

    public struct Uint4
    {
        public uint data1;
        public uint data2;
        public uint data3;
        public uint data4;
    }


    public struct Uint8
    {
        public uint data1;
        public uint data2;
        public uint data3;
        public uint data4;
        public uint data5;
        public uint data6;
        public uint data7;
        public uint data8;
    }
}
