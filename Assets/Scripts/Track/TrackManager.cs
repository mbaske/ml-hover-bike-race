using UnityEngine;
using UnityEngine.Rendering;
using BezierSolution;
using EasyButtons;

namespace Hoverbike.Track
{
    // Track lengths and locations are not defined in world space, but as
    // - normalized t (see spline) -> 0 - 1
    // - degrees (used for wrapping) -> 0 - 359
    // - position list -> n = chunk count * rings per chunk = vertex count along spline tangent
    // - segment list (groups of positions) -> n = position count / segment length 
    // 
    // Distances between positions & segments are compressed in curves.

    public class TrackManager : MonoBehaviour
    {
        public Lanes NormLanes { get; private set; }
        public SearchableLocations<Segment> Segments { get; private set; }
        public SearchableLocations<Position> Positions { get; private set; }

        [Header("Extrusion")]
        [SerializeField, Range(2, 8), Tooltip("Pipe width / 2")]
        private float radiusX = 5;
        [SerializeField, Range(1, 4), Tooltip("Pipe height / 2")]
        private float radiusY = 2f;
        [SerializeField, Range(0, 1), Tooltip("Curvature twist amount")]
        private float twist = 0.5f;
        [SerializeField, Range(0, 1), Tooltip("Curvature twist smoothing")]
        private float smoothing = 0.25f;
        private BezierSpline spline;

        [Header("Chunks")]
        [SerializeField, Range(4, 64), Tooltip("Number of mesh chunks")]
        private int chunkCount = 32;
        [SerializeField, Range(8, 256), Tooltip("Number of vertex rings (ellipses) per chunk")]
        private int ringsPerChunk = 32;
        [SerializeField, Range(12, 72), Tooltip("Number of vertices per ring (ellipse smoothness)")]
        private int verticesPerRing = 18;

        [Header("Mesh & Rendering")]
        [SerializeField, Range(0.5f, 1f), Tooltip("Mesh collider extent, 0.5 = bottom half of pipe")]
        private float colliderExtent = 0.7f;
        [SerializeField]
        private bool generateOutsideMesh;

        // OPTIONAL STRIPE TEXTURES
        [SerializeField, Tooltip("Draw stripe textures after randomization (expensive, disable during training)")]
        private bool generateTextures;
        [SerializeField]
        private Texture2D texture;
        private Color32[][] stripeColors;
        private MaterialPropertyBlock mpBlock;
        private int texIndex;
        private Lanes texLanes;
        private bool texPending;

        [SerializeField]
        private Material insideMaterial;
        [SerializeField]
        private Material outsideMaterial;
        [SerializeField]
        private Color32 boostStripeColor;
        [SerializeField]
        private Color32 slowStripeColor;
        [SerializeField]
        private Color32 laneGapColor;
        [SerializeField, Tooltip("DEBUG")]
        private string meshInfo;
        private int totalVtxCount;
        private int totalTriCount;

        [Header("Lanes & Segments")]
        [SerializeField, Range(2, 8), Tooltip("Number of lanes")]
        private int laneCount = 4;
        [SerializeField, Range(0, 1f), Tooltip("Lane extent, 1 = lanes cover full track width")]
        private float laneExtent = 0.8f;
        [SerializeField, Range(0, 1), Tooltip("Width of gaps between lanes, 1 = gaps are as wide as lanes")]
        private float laneGap = 0.1f;
        private Lanes gizmoLanes;

        [SerializeField, Range(1, 8), Tooltip("Segments contain boost or slow down values")]
        private int segmentLength = 4;
        private int segmentsPerChunk;
        [SerializeField, Range(0.1f, 1), Tooltip("Minimum boost value")]
        private float minBoostStrength = 0.1f;
        [SerializeField, Range(0.1f, 1), Tooltip("Maximum boost value")]
        private float maxBoostStrength = 1f;
        [SerializeField, Range(0.1f, 1), Tooltip("Minimum slow down value")]
        private float minSlowStrength = 0.1f;
        [SerializeField, Range(0.1f, 1), Tooltip("Maximum slow down value")]
        private float maxSlowStrength = 1f;
        [SerializeField, Range(0, 1), Tooltip("Random distribution: slow down vs boost ratio")]
        private float slowToBoostStripeRatio = 0.5f;
        [SerializeField, Range(1, 32), Tooltip("Minimum stripe length in segments")]
        private int minStripeLength = 4;
        [SerializeField, Range(1, 32), Tooltip("Maximum stripe length in segments")]
        private int maxStripeLength = 8;
        [SerializeField, Range(1, 32), Tooltip("Minimum number of segments between stripes")]
        private int minStripeSpace = 4;
        [SerializeField, Range(1, 32), Tooltip("Maximum number of segments between stripes")]
        private int maxStripeSpace = 16;
        [SerializeField, Range(0, 0.2f), Tooltip("Strip start probability")]
        private float stripeStartProbablility = 0.05f;
        [SerializeField, Range(0, 0.5f), Tooltip("Strip stop probability")]
        private float stripeStopProbablility = 0.25f;

        private void Awake()
        {
            Refresh();
            Randomize();
        }

        private void Update()
        {
            if (generateTextures && texPending)
            {
                DrawTexture();
                texIndex++;
                texPending = texIndex < chunkCount;
            }
        }

        private void Refresh()
        {
            spline = spline ?? FindObjectOfType<BezierSpline>();
            spline.Refresh();

            int n = chunkCount * ringsPerChunk;
            Positions = new SearchableLocations<Position>(n);
            for (int i = 0; i < n; i++)
            {
                Positions.Add(new Position(1f / n * i, spline));
            }

            int avgRange = Mathf.Max(1, Mathf.RoundToInt(Positions.Count / 2 * smoothing));
            float twistAmount = twist * (chunkCount * ringsPerChunk / 8); // TBD
            for (int i = 0; i < n; i++)
            {
                Positions[i].Rotate(Positions, avgRange, twistAmount);
            }

            n /= segmentLength;
            Segments = new SearchableLocations<Segment>(n);
            for (int i = 0; i < n; i++)
            {
                Segments.Add(new Segment(
                    Positions[i * segmentLength],
                    Positions[(i + 1) * segmentLength],
                    laneCount));
            }

            NormLanes = new Lanes(laneCount, laneExtent, laneGap);
            gizmoLanes = NormLanes.Scale(radiusX);

            // Same for all locations, constant track width.
            TrackLocation.OrthoLength = radiusX;
        }

        [Button]
        private void UpdateMeshes()
        {
            ClearNestedContents();
            Refresh();
            ExtrudeSpline();

            meshInfo = $"{totalVtxCount} Vertices, {totalTriCount} Triangles";
        }

        // [Button]
        // private void RandomizeSegments()
        // {
        //     Refresh();
        //     Randomize();
        // }

        public void Randomize()
        {
            int n = NormLanes.Count;
            float[] boost = new float[n];
            int[] count = new int[n];

            foreach (Segment segment in Segments)
            {
                for (int i = 0; i < n; i++)
                {
                    if (Mathf.Abs(boost[i]) > 0)
                    {
                        if (count[i] >= minStripeLength &&
                           (count[i] == maxStripeLength || RndFlip(stripeStopProbablility)))
                        {
                            count[i] = 0;
                            boost[i] = 0;
                        }
                    }
                    else
                    {
                        if (-count[i] >= minStripeSpace &&
                           (-count[i] == maxStripeSpace || RndFlip(stripeStartProbablility)))
                        {
                            count[i] = 0;
                            boost[i] = RndFlip(slowToBoostStripeRatio)
                                ? Random.Range(minBoostStrength, maxBoostStrength)
                                : -Random.Range(minSlowStrength, maxSlowStrength);
                        }
                    }

                    segment.BoostValues[i] = boost[i];
                    count[i] += Mathf.Abs(boost[i]) > 0 ? 1 : -1;
                }
            }

            if (generateTextures)
            {
                GenerateTextures();
            }
        }


        private void GenerateTextures()
        {
            mpBlock = new MaterialPropertyBlock();
            texLanes = NormLanes.ScaleInt(texture.width / 4, texture.width / 2);
            texIndex = 0;
            texPending = true;

            CalcStripeColors();
        }

        private void CalcStripeColors()
        {
            stripeColors = new Color32[Segments.Count][];
            Color32 nullCol = new Color32(0, 0, 0, 0);

            foreach (Segment segment in Segments)
            {
                stripeColors[segment.Index] = new Color32[texLanes.Count];
                foreach (Lane lane in texLanes)
                {
                    float val = segment.BoostValues[lane.index];
                    if (val.Equals(0f))
                    {
                        stripeColors[segment.Index][lane.index] = nullCol;
                    }
                    else
                    {
                        Color32 col = val > 0 ? boostStripeColor : slowStripeColor;
                        val = Mathf.Abs(val);
                        col.r = (byte)(col.r * val);
                        col.g = (byte)(col.g * val);
                        col.b = (byte)(col.b * val);
                        stripeColors[segment.Index][lane.index] = col;
                    }
                }
            }
        }

        // TODO Jobify or shader.
        private void DrawTexture()
        {
            int width = texture.width;
            int height = texture.height;
            int segHeight = height / segmentsPerChunk;

            Texture2D tex = new Texture2D(width, height, texture.format, true);
            Color32[] texColors = texture.GetPixels32();
            int segOffset = texIndex * segmentsPerChunk;

            // Stripes.
            for (int i = 0; i < segmentsPerChunk; i++)
            {
                int yMin = i * segHeight;
                int yMax = (i + 1) * segHeight;

                for (int y = yMin; y < yMax; y++)
                {
                    int d = y % 32;
                    if (d > 7 && d < 24) // TBD stripe placement & density.
                    {
                        int iSeg = segOffset + i;
                        foreach (Lane lane in texLanes)
                        {
                            Color32 col = stripeColors[iSeg][lane.index];
                            if (col.a > 0)
                            {
                                for (int x = lane.minInt; x < lane.maxInt; x++)
                                {
                                    // TODO inv. x pos?
                                    texColors[(y + 1) * width - x] = col;
                                }
                            }
                        }
                    }
                }
            }

            // Gaps.
            for (int y = 0; y < height; y++)
            {
                for (int i = 0, n = texLanes.Count - 1; i < n; i++)
                {
                    int xMin = texLanes[i].maxInt;
                    int xMax = texLanes[i + 1].minInt;

                    for (int x = xMin; x < xMax; x++)
                    {
                        texColors[y * width + x] = laneGapColor;
                    }
                }
            }

            tex.SetPixels32(texColors);
            tex.Apply();

            mpBlock.SetTexture("_MainTex", tex);
            mpBlock.SetColor("_Color", Color.white);
            int iTex = texIndex * (generateOutsideMesh ? 2 : 1);
            transform.GetChild(iTex).GetComponent<MeshRenderer>().SetPropertyBlock(mpBlock);
        }


        private void ClearNestedContents()
        {
            while (transform.childCount > 0)
            {
                DestroyImmediate(transform.GetChild(0).gameObject);
            }
            totalVtxCount = 0;
            totalTriCount = 0;
        }

        private void ExtrudeSpline()
        {
            float chunkIncr = 1f / (float)chunkCount;
            for (int iChunk = 0; iChunk < chunkCount; iChunk++)
            {
                // Center chunk position.
                Vector3 chunkPos = spline.GetPoint((iChunk + 0.5f) * chunkIncr);
                // + 1 -> Connecting ring.
                Vector3[][] vertices = new Vector3[ringsPerChunk + 1][];

                for (int iRing = 0; iRing <= ringsPerChunk; iRing++)
                {
                    vertices[iRing] = CreateEllipseAt(
                        Positions[iChunk * ringsPerChunk + iRing], chunkPos);
                }
                CreateChunkAt(iChunk, vertices, chunkPos);
            }
        }

        private Vector3[] CreateEllipseAt(Position trackPos, Vector3 chunkPos)
        {
            Vector3 center = trackPos.PosV3 - chunkPos;
            float angleIncr = 360f / (float)(verticesPerRing);
            // + 1 -> Connecting vertex.
            Vector3[] ellipse = new Vector3[verticesPerRing + 1];

            for (int i = 0; i <= verticesPerRing; i++)
            {
                // Start with top vertex.
                float t = (i * angleIncr - 90f) * Mathf.Deg2Rad;
                ellipse[i] = center + trackPos.Rotation * new Vector3(
                    Mathf.Cos(t) * radiusX, -Mathf.Sin(t) * radiusY, 0);
            }
            return ellipse;
        }

        private void CreateChunkAt(int i, Vector3[][] vertices, Vector3 chunkPos)
        {
            GameObject chunk = new GameObject("inside_" + i);
            chunk.layer = Layers.Ground;
            chunk.transform.parent = transform;
            chunk.transform.position = chunkPos;

            MeshRenderer renderer = chunk.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = insideMaterial;
            renderer.receiveShadows = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            chunk.AddComponent<MeshFilter>().sharedMesh = CreateMesh(vertices, true, false);
            chunk.AddComponent<MeshCollider>().sharedMesh = CreateMesh(vertices, true, true);

            if (generateOutsideMesh)
            {
                chunk = new GameObject("outside_" + i);
                chunk.layer = Layers.Ground;
                chunk.transform.parent = transform;
                chunk.transform.position = chunkPos;

                renderer = chunk.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = outsideMaterial;
                renderer.receiveShadows = false;
                renderer.shadowCastingMode = ShadowCastingMode.Off;

                chunk.AddComponent<MeshFilter>().sharedMesh = CreateMesh(vertices, false, false);
            }
        }

        private Mesh CreateMesh(Vector3[][] vertices, bool isCCW, bool isCollider)
        {
            int nRing = vertices.Length;
            int nVtx = vertices[0].Length;
            int minVtx = 0;
            int maxVtx = nVtx;
            float uvRing = nRing - 1f;
            float uvVtx = nVtx - 1f;

            if (isCollider)
            {
                int n = Mathf.RoundToInt(nVtx * colliderExtent) / 2 * 2;
                minVtx = (nVtx - n - 1) / 2;
                maxVtx = nVtx - minVtx;
                nVtx = maxVtx - minVtx;
            }

            Vector2[] uvs = new Vector2[nRing * nVtx];
            Vector3[] verts = new Vector3[nRing * nVtx];
            int[] tris = new int[(nRing - 1) * (nVtx - 1) * 6];

            for (int i = 0, iTri = 0, iRing = 0; iRing < nRing; iRing++)
            {
                for (int iVtx = minVtx; iVtx < maxVtx; iVtx++)
                {
                    if (iRing > 0 && iVtx > minVtx)
                    {
                        tris[iTri++] = isCCW ? i : i - 1 - nVtx;
                        tris[iTri++] = i - 1;
                        tris[iTri++] = isCCW ? i - 1 - nVtx : i;
                        tris[iTri++] = isCCW ? i : i - nVtx;
                        tris[iTri++] = i - 1 - nVtx;
                        tris[iTri++] = isCCW ? i - nVtx : i;
                    }

                    uvs[i] = new Vector2(iVtx / uvVtx, iRing / uvRing);
                    verts[i++] = vertices[iRing][iVtx];
                }
            }

            Mesh mesh = new Mesh();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();

            totalVtxCount += verts.Length;
            totalTriCount += tris.Length / 3;

            return mesh;
        }

        private void OnValidate()
        {
            chunkCount = NearestPowerOfTwo(chunkCount);
            ringsPerChunk = NearestPowerOfTwo(ringsPerChunk);
            segmentLength = NearestPowerOfTwo(segmentLength);
            segmentsPerChunk = ringsPerChunk / segmentLength;
            verticesPerRing = Mathf.RoundToInt(verticesPerRing / 6f) * 6;
            minBoostStrength = Mathf.Min(minBoostStrength, maxBoostStrength);
            maxBoostStrength = Mathf.Max(minBoostStrength, maxBoostStrength);
            minSlowStrength = Mathf.Min(minSlowStrength, maxSlowStrength);
            maxSlowStrength = Mathf.Max(minSlowStrength, maxSlowStrength);
            minStripeLength = Mathf.Min(minStripeLength, maxStripeLength);
            maxStripeLength = Mathf.Max(minStripeLength, maxStripeLength);
            minStripeSpace = Mathf.Min(minStripeSpace, maxStripeSpace);
            maxStripeSpace = Mathf.Max(minStripeSpace, maxStripeSpace);
        }

        private void OnDrawGizmos()
        {
            if (gizmoLanes != null)
            {
                foreach (Segment segment in Segments)
                {
                    foreach (Lane lane in gizmoLanes)
                    {
                        float val = segment.BoostValues[lane.index];
                        Color32 col = val > 0 ? boostStripeColor : slowStripeColor;
                        col.a = (byte)(Mathf.Abs(val) * 255);
                        Gizmos.color = col;

                        Vector3 a = segment.MinV3 + segment.Orthogonal * lane.min;
                        Vector3 b = segment.MinV3 + segment.Orthogonal * lane.max;
                        Vector3 c = segment.MaxV3 + segment.Orthogonal * lane.min;
                        Vector3 d = segment.MaxV3 + segment.Orthogonal * lane.max;

                        Gizmos.DrawLine(a, b);
                        Gizmos.DrawLine(b, d);
                        Gizmos.DrawLine(d, c);
                        Gizmos.DrawLine(c, a);
                    }
                }
            }
        }

        private static bool RndFlip(float probability = 0.5f)
        {
            return Random.value <= probability;
        }

        // https://stackoverflow.com/questions/4398711/round-to-the-nearest-power-of-two/4398799
        private static int NearestPowerOfTwo(int n)
        {
            int v = n;
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v++; // next power of 2
            int x = v >> 1; // previous power of 2
            return (v - n) > (n - x) ? x : v;
        }
    }
}