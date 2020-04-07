using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace TombLib.Utils
{
    public static class VectorUtils
    {
        public static Vector3 ToVector3 (Vector4 rhs)
        {
            Vector3 c = new Vector3(rhs.X,rhs.Y,rhs.Z); //Internally call Currency constructor
            return c;
        }
    }
}
