using UnityEngine;
using BezierSolution;

namespace Hoverbike.Track
{
    public class Position : TrackLocation
    {
        public float Curvature { get; private set; }
        public Quaternion Rotation { get; private set; }

        public Position(float t, BezierSpline spline)
        {
            PosT = t;
            PosV3 = spline.GetPoint(t);
            PosDeg = t * 360f;
            Tangent = spline.GetTangent(t).normalized;
            TangentXZ = Vector3.ProjectOnPlane(Tangent, Vector3.up);
        }

        public void Rotate(SearchableLocations<Position> list, int avgRange, float twistAmount)
        {
            Curvature = 0;
            for (int i = Index - avgRange, j = Index + avgRange; i < j; i++)
            {
                Curvature += Vector3.SignedAngle(
                    list[i + 1].TangentXZ, 
                    list[i].TangentXZ, 
                    Vector3.up);
            }
            Curvature /= (float)(avgRange * 2);

            Quaternion glbYRot = Quaternion.FromToRotation(Vector3.forward, TangentXZ);
            Quaternion locXRot = Quaternion.FromToRotation(TangentXZ, Tangent);
            Quaternion locZRot = Quaternion.AngleAxis(Curvature * twistAmount, Vector3.forward);
            Rotation = glbYRot * locXRot * locZRot;
            Orthogonal = Rotation * Vector3.right;
            Normal = Rotation * Vector3.up;
        }
    }
}