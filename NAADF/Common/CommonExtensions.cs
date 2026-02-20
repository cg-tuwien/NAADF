using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.Common
{
    public static class CommonExtensions
    {
        public static void setCameraPos(this Effect effect, PositionSplit pos)
        {
            effect.Parameters["camPosIntX"].SetValue(pos.integer.X);
            effect.Parameters["camPosIntY"].SetValue(pos.integer.Y);
            effect.Parameters["camPosIntZ"].SetValue(pos.integer.Z);
            effect.Parameters["camPosFrac"].SetValue(pos.frac);
        }
    }
}
