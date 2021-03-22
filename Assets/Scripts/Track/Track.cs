using BezierSolution;
using EasyButtons;
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MBaske.Hoverbikes
{
    public class Track : MonoBehaviour
    {
        [Serializable]
        public struct Point
        {
            public int Index;
            public Quaternion Rotation;
            public Vector3 Position;
            public Vector3 Forward;
            public Vector3 ForwardXZ;
            public Vector3 Right;
            public Vector3 Up;
            public float AvgAngle;
            public float Distance;
            public float CumlLength;
            public float CumlLengthRatio;
        }
        [SerializeField, HideInInspector]
        private Point[] m_Points;

        public Point GetPoint(int i)
        {
            return m_Points[(i + Length) % Length];
        }

        public int Length => m_Points.Length;
        public float HalfWidth => m_PipeRadiusX;

        // Slightly larger than number of points.
        private float m_MeasuredLength;

        [SerializeField]
        private float m_PipeRadiusX = 5;
        [SerializeField]
        private float m_PipeRadiusY = 2;
        [SerializeField]
        private float m_PipeTwist = 100;
        [SerializeField]
        private int m_NumVertices = 36;

        private void OnValidate()
        {
            m_NumVertices = Mathf.RoundToInt(m_NumVertices / 6f) * 6;
        }

        private float m_EllipseCirc;
        private float[] m_EllipseUVXFull;
        private Vector3[] m_EllipseVtxFull;
        private float[] m_EllipseUVXHalf;
        private Vector3[] m_EllipseVtxHalf;

        [SerializeField]
        private MeshFilter m_OuterMesh;
        [SerializeField]
        private MeshFilter m_InnerMesh;
        [SerializeField]
        private MeshCollider m_Collider;


        // SEARCH

        public Point FindPoint(Vector3 pos, Point lastKnown, bool searchBackwards = true)
        {
            int i = lastKnown.Index; 
            float d0 = (pos - lastKnown.Position).sqrMagnitude;
            float d1 = d0;
            bool search = true;

            while (search)
            {
                float d2 = (pos - GetPoint(++i).Position).sqrMagnitude;
                search = d2 < d1;
                d1 = d2;
            }
            i--;

            if (searchBackwards && i == lastKnown.Index)
            {
                d1 = d0;
                search = true;

                while (search)
                {
                    float d2 = (pos - GetPoint(--i).Position).sqrMagnitude;
                    search = d2 < d1;
                    d1 = d2;
                }
                i++;
            }

            return GetPoint(i);
        }

        // Can't do C# List.BinarySearch over all points, because nearby positions
        // aren't necessarily neighbouring points on a track that curves in on itself.
        // Need to constrain min/max indices first, using a coarse incremental search.

        public Point FindPoint(Vector3 pos, int incr = -1)
        {
            incr = incr == -1 ? Length / 100 : incr;
            int i = IncrementalSearch(pos, incr);
            i = BinarySearch(pos, i - incr, i + incr);
            return GetPoint(i);
        }

        private int IncrementalSearch(Vector3 pos, int incr)
        {
            int index = 0;
            float min = Mathf.Infinity;

            for (int i = 0; i < Length; i += incr)
            {
                float d = (pos - GetPoint(i).Position).sqrMagnitude;
                if (d < min)
                {
                    min = d;
                    index = i;
                }
            }
            return index;
        }

        private int BinarySearch(Vector3 pos, int min, int max)
        {
            bool b = (pos - GetPoint(min).Position).sqrMagnitude
                   < (pos - GetPoint(max).Position).sqrMagnitude;
            int delta = max - min;
            int mid = min + delta / 2;
            return delta == 1
                ? (b ? min : max)
                : BinarySearch(pos, b ? min : mid, b ? mid : max);
        }


        // UPDATE VIA BUTTON AFTER EDITING SPLINE

#if (UNITY_EDITOR)
        [Button]
        private void UpdateTrack()
        {
            ParseSpline();
            CalcEllipses();
            UpdateMesh(m_OuterMesh.sharedMesh, true);
            UpdateMesh(m_InnerMesh.sharedMesh, false);
            UpdateMesh(m_Collider.sharedMesh, false, true);
        }

        private void ParseSpline()
        {
            var spline = FindObjectOfType<BezierSpline>();

            int n = Mathf.FloorToInt(spline.Length);
            m_Points = new Point[n];

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                m_Points[i].Position = spline.GetPoint(t);
                m_Points[i].Index = i;
            }

            m_MeasuredLength = 0;
            for (int i = 0; i < n; i++)
            {
                m_Points[i].Forward = m_Points[(i + 1) % n].Position - m_Points[i].Position;
                m_Points[i].ForwardXZ = Vector3.ProjectOnPlane(m_Points[i].Forward, Vector3.up).normalized;
                m_Points[i].Distance = m_Points[i].Forward.magnitude;
                m_Points[i].Forward.Normalize();
                m_MeasuredLength += m_Points[i].Distance;
                m_Points[i].CumlLength = m_MeasuredLength;
            }

            // Smooth out angle.
            int avgExt = Mathf.Max(1, n / 64); // TBD

            for (int i = 0; i < n; i++)
            {
                float angleSum = 0;
                for (int j = -avgExt; j < avgExt; j++)
                {
                    int k = i + j + n;
                    angleSum += Vector3.SignedAngle(
                        m_Points[k % n].ForwardXZ,
                        m_Points[(k + 1) % n].ForwardXZ,
                        Vector3.up);
                }
                m_Points[i].AvgAngle = angleSum / (avgExt * 2f);
            }

            for (int i = 0; i < n; i++)
            {
                Vector3 normal = Vector3.Cross(m_Points[i].ForwardXZ, Vector3.up);
                m_Points[i].Right = Quaternion.AngleAxis(m_Points[i].AvgAngle * -m_PipeTwist, m_Points[i].Forward) * normal;
                m_Points[i].Up = Vector3.Cross(m_Points[i].Right, m_Points[i].Forward);
                m_Points[i].Rotation = Quaternion.LookRotation(m_Points[i].Forward, m_Points[i].Up);
                m_Points[i].CumlLengthRatio = m_Points[i].CumlLength / m_MeasuredLength;
            }
        }

        private void CalcEllipses()
        {
            float angleFrac = 360f / m_NumVertices * Mathf.Deg2Rad;

            int n = m_NumVertices + 1;
            float angleOffset = 90 * Mathf.Deg2Rad;
            float[] lengths = new float[n];
            float circumference = 0;
            float uvx = 0;

            m_EllipseUVXFull = new float[n];
            m_EllipseVtxFull = new Vector3[n];
            
            for (int i = 0; i < n; i++)
            {
                float angle = angleOffset + i * angleFrac;
                m_EllipseVtxFull[i] = new Vector2(
                        Mathf.Cos(angle) * m_PipeRadiusX,
                        Mathf.Sin(angle) * m_PipeRadiusY);
                if (i > 0)
                {
                    lengths[i] = (m_EllipseVtxFull[i]
                        - m_EllipseVtxFull[i - 1]).magnitude;
                    circumference += lengths[i];
                }
            }
            for (int i = 1; i < n; i++)
            {
                uvx += lengths[i] / circumference;
                m_EllipseUVXFull[i] = uvx;
            }
            m_EllipseCirc = circumference;


            n = m_NumVertices / 2 + 1;
            angleOffset = 180 * Mathf.Deg2Rad;
            lengths = new float[n];
            circumference = 0;
            uvx = 0;

            m_EllipseUVXHalf = new float[n];
            m_EllipseVtxHalf = new Vector3[n];
            // Shrink a bit to avoid texture glitches.
            float shrink = 0.999f;

            for (int i = 0; i < n; i++)
            {
                float angle = angleOffset + i * angleFrac;
                m_EllipseVtxHalf[i] = new Vector2(
                        Mathf.Cos(angle) * m_PipeRadiusX * shrink,
                        Mathf.Sin(angle) * m_PipeRadiusY * shrink);
                if (i > 0)
                {
                    lengths[i] = (m_EllipseVtxHalf[i]
                        - m_EllipseVtxHalf[i - 1]).magnitude;
                    circumference += lengths[i];
                }
            }
            for (int i = 1; i < n; i++)
            {
                uvx += lengths[i] / circumference;
                m_EllipseUVXHalf[i] = uvx;
            }
        }

        private void UpdateMesh(Mesh mesh, bool ccw, bool half = false)
        {
            int nSpline = Length;
            // Add. point close ellipse uv loop.
            int nEllipse = (half ? m_NumVertices / 2 : m_NumVertices) + 1;
            // nSpline + 1 -> Add. point close spline uv loop.
            var vertices = new Vector3[nEllipse * (nSpline + 1)];
            var triangles = new int[nEllipse * nSpline * 6];
            var uvs = new Vector2[vertices.Length];
            // Half pipe (stripes/collider) -> uv.y 0-1 extends over whole track.
            // Otherwise uv.y is set to match ellipse circumference.
            float uvRatio = half ? 1 : m_MeasuredLength / m_EllipseCirc;
            Vector2 uv = Vector2.zero;

            for (int i = 0, j = 0; i <= nSpline; i++)
            {
                int iCrntPoint = i % nSpline;
                Vector3 pos = m_Points[iCrntPoint].Position;
                Quaternion rot = m_Points[iCrntPoint].Rotation;

                int iFirstVtxCrntEllipse = i * nEllipse;
                int iFirstVtxNextEllipse = (i + 1) % (nSpline + 1) * nEllipse;

                for (int iCrntVtx = 0; iCrntVtx < nEllipse; iCrntVtx++)
                {
                    int iVtx0 = iFirstVtxCrntEllipse + iCrntVtx;

                    vertices[iVtx0] = pos + rot * (half 
                        ? m_EllipseVtxHalf[iCrntVtx]
                        : m_EllipseVtxFull[iCrntVtx]);

                    uv.x = half
                        ? m_EllipseUVXHalf[iCrntVtx]
                        : m_EllipseUVXFull[iCrntVtx];
                    uv.x = ccw ? 1 - uv.x : uv.x;
                    uvs[iVtx0] = uv;

                    if (i < nSpline && (!half || iCrntVtx < nEllipse - 1))
                    {
                        int iNextVtx = (iCrntVtx + 1) % nEllipse;
                        int iVtx1 = iFirstVtxCrntEllipse + iNextVtx;
                        int iVtx2 = iFirstVtxNextEllipse + iCrntVtx;
                        int iVtx3 = iFirstVtxNextEllipse + iNextVtx;

                        if (ccw)
                        {
                            triangles[j++] = iVtx0;
                            triangles[j++] = iVtx3;
                            triangles[j++] = iVtx2;

                            triangles[j++] = iVtx0;
                            triangles[j++] = iVtx1;
                            triangles[j++] = iVtx3;
                        }
                        else
                        {
                            triangles[j++] = iVtx0;
                            triangles[j++] = iVtx2;
                            triangles[j++] = iVtx3;

                            triangles[j++] = iVtx0;
                            triangles[j++] = iVtx3;
                            triangles[j++] = iVtx1;
                        }
                    }
                }
                uv.y = m_Points[iCrntPoint].CumlLengthRatio * uvRatio;
            }

            mesh.Clear();
            mesh.indexFormat = vertices.Length > 65536 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        //private void OnDrawGizmosSelected()
        //{
        //    for (int i = 0; i < m_Points.Length; i++)
        //    {
        //        Vector3 p = m_Points[i].Position;
        //        Gizmos.color = Color.red;
        //        Gizmos.DrawRay(p, m_Points[i].Right * m_PipeRadiusX);
        //        Gizmos.color = Color.green;
        //        Gizmos.DrawRay(p, m_Points[i].Up * m_PipeRadiusY);
        //        Gizmos.color = Color.blue;
        //        Gizmos.DrawRay(p, m_Points[i].Forward);
        //        Gizmos.color = Color.cyan;
        //        Gizmos.DrawRay(p, m_Points[i].ForwardXZ);
        //        Gizmos.color = Color.white;
        //        Gizmos.DrawSphere(p, 0.05f);
        //    }
        //}
#endif
    }
}