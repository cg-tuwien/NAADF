using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.Common
{
    public class Cube : VertexBuffer
    {
        public Cube(GraphicsDevice device) : base(device, VertexPosition.VertexDeclaration, 36, BufferUsage.WriteOnly)
        {
            Vector3 pos1 = new Vector3(0, 0, 0);
            Vector3 pos2 = new Vector3(0, 0, 1);
            Vector3 pos3 = new Vector3(1, 0, 1);
            Vector3 pos4 = new Vector3(1, 0, 0);
            Vector3 pos5 = new Vector3(0, 1, 0);
            Vector3 pos6 = new Vector3(0, 1, 1);
            Vector3 pos7 = new Vector3(1, 1, 1);
            Vector3 pos8 = new Vector3(1, 1, 0);



            //Adding Vertexes of Skybox Cube
            List<VertexPosition> vertexes = new List<VertexPosition>();
            vertexes.AddRange(new[] { new VertexPosition(pos1), new VertexPosition(pos2), new VertexPosition(pos6), new VertexPosition(pos1), new VertexPosition(pos6), new VertexPosition(pos5) });
            vertexes.AddRange(new[] { new VertexPosition(pos2), new VertexPosition(pos3), new VertexPosition(pos7), new VertexPosition(pos2), new VertexPosition(pos7), new VertexPosition(pos6) });
            vertexes.AddRange(new[] { new VertexPosition(pos3), new VertexPosition(pos4), new VertexPosition(pos8), new VertexPosition(pos3), new VertexPosition(pos8), new VertexPosition(pos7) });
            vertexes.AddRange(new[] { new VertexPosition(pos1), new VertexPosition(pos5), new VertexPosition(pos8), new VertexPosition(pos1), new VertexPosition(pos8), new VertexPosition(pos4) });
            vertexes.AddRange(new[] { new VertexPosition(pos7), new VertexPosition(pos5), new VertexPosition(pos6), new VertexPosition(pos5), new VertexPosition(pos7), new VertexPosition(pos8) });
            vertexes.AddRange(new[] { new VertexPosition(pos1), new VertexPosition(pos3), new VertexPosition(pos2), new VertexPosition(pos3), new VertexPosition(pos1), new VertexPosition(pos4) });

            SetData(vertexes.ToArray());
        }
    }
}
