using UnityEngine;

namespace Hoverbike.Track
{
    public class Segment : TrackLocation
    {
        public Vector3 MinV3 { get; private set; }
        public Vector3 MaxV3 { get; private set; }
        public Vector3 DeltaV3 { get; private set; }

        public float[] BoostValues { get; private set; }

        public Segment(TrackLocation from, TrackLocation to, int laneCount)
        {
            PosT = Mathf.Lerp(from.PosT, to.PosT, 0.5f);
            PosV3 = Vector3.Lerp(from.PosV3, to.PosV3, 0.5f);
            PosDeg = Mathf.Lerp(from.PosDeg, to.PosDeg, 0.5f);
            Normal = Vector3.Lerp(from.Normal, to.Normal, 0.5f);
            Tangent = Vector3.Lerp(from.Tangent, to.Tangent, 0.5f);
            TangentXZ = Vector3.Lerp(from.TangentXZ, to.TangentXZ, 0.5f);
            Orthogonal = Vector3.Lerp(from.Orthogonal, to.Orthogonal, 0.5f);

            MinV3 = from.PosV3;
            MaxV3 = to.PosV3;
            DeltaV3 = MaxV3 - MinV3;

            BoostValues = new float[laneCount];
        }
    }
}