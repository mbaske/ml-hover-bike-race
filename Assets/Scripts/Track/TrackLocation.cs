using UnityEngine;

namespace Hoverbike.Track
{
    public abstract class TrackLocation
    {
        // Track width is constant -> every orthogonal's length = ellipse radiusX.
        public static float OrthoLength { get; set; }

        public int Index { get; set; }
        public float PosT { get; protected set; } // 0 - 1
        public float PosDeg { get; protected set; } // 0 - 359
        public Vector3 PosV3 { get; protected set; } // world pos
        public Vector3 Normal { get; protected set; } // up
        public Vector3 Tangent { get; protected set; } // forward
        public Vector3 TangentXZ { get; protected set; } // projected
        public Vector3 Orthogonal { get; protected set; } // to normal & tangent

        public float GetNormLocalXPos(Vector3 pV3)
        {
            pV3 = Vector3.ProjectOnPlane(pV3 - PosV3, Normal);
            pV3 = Vector3.ProjectOnPlane(pV3, Tangent);
            return pV3.magnitude * Mathf.Sign(Vector3.Dot(pV3, Orthogonal)) / OrthoLength;
        }

        public float GetNormLocalYAngle(Vector3 fwd)
        {
            return Vector3.SignedAngle(Tangent, fwd, Normal) / 180f;
        }

        public void Draw()
        {
            Debug.DrawRay(PosV3, Normal, Color.blue);
            Debug.DrawRay(PosV3, Tangent, Color.red);
            Debug.DrawRay(PosV3, Orthogonal, Color.white);
        }
    }
}