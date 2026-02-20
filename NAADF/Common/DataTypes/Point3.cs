using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.Common
{
    public struct Point3 : IEquatable<Point3>
    {
        public int X, Y, Z;

        public Point3()
        {
            X = 0;
            Y = 0;
            Z = 0;
        }


        public Point3(int XYZ)
        {
            X = XYZ;
            Y = XYZ;
            Z = XYZ;
        }


        public Point3(int X, int Y, int Z)
        {
            this.X = X;
            this.Y = Y;
            this.Z = Z;
        }

        public bool Equals(Point3 other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object? obj) => obj is Point3 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X;
                hash = (hash * 397) ^ Y;
                hash = (hash * 397) ^ Z;
                return hash;
            }
        }

        public Vector3 ToVector3()
        {
            return new Vector3(X, Y, Z);
        }

        public static Point3 FromVector3(Vector3 vec)
        {
            return new Point3((int)vec.X, (int)vec.Y, (int)vec.Z);
        }

        public static Point3 operator +(Point3 a, Point3 b)
        {
            return new Point3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static Point3 operator *(Point3 a, int mul)
        {
            return new Point3(a.X * mul, a.Y * mul, a.Z * mul);
        }

        public static Point3 operator /(Point3 a, int div)
        {
            return new Point3(a.X / div, a.Y / div, a.Z / div);
        }

        public static Point3 operator %(Point3 a, int div)
        {
            return new Point3(a.X % div, a.Y % div, a.Z % div);
        }

        public static Point3 operator -(Point3 a, Point3 b)
        {
            return new Point3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }
    }
}
