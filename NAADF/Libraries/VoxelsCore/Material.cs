using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxels
{
    public struct Material
    {
        public float emit;
        public float flux;
        public float metalic;
        public float roughness;
        public float ior;

        public Material()
        {
            emit = 0;
            flux = 0;
            metalic = 0;
            roughness = 0;
            ior = 0;
        }
    }
}
