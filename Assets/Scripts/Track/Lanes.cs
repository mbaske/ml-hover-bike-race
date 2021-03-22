using System;
using UnityEngine;
using EasyButtons;
using Random = UnityEngine.Random;

namespace MBaske.Hoverbikes
{
    public class Lanes : MonoBehaviour
    {
        public const int NumLanes = 4;

        private const int c_TextureWidth = 32;
        private const int c_TextureHeight = 8192;

        [SerializeField]
        private float m_Ratio = 0.5f;

        [Space, SerializeField]
        private int m_MinSpacing = 10;
        [SerializeField]
        private int m_MaxSpacing = 40;

        [Space, SerializeField]
        private int m_MinLength = 5;
        [SerializeField]
        private int m_MaxLength = 15;

        [Space, SerializeField]
        private int m_DrawOffset = 0;

        [Space, SerializeField]
        private Color m_Positive;
        [SerializeField]
        private Color m_Negative;

        [SerializeField]
        private Material m_Material;
        private Texture2D m_Texture;
        private Color32[] m_Colors;

        private Track m_Track;
        private int m_TrackLength;
        private int[,] m_BoostValues;


        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            m_Track = GetComponent<Track>();
            m_TrackLength = m_Track.Length;
            m_BoostValues = new int[NumLanes, m_TrackLength];

            m_Texture = new Texture2D(32, c_TextureHeight, TextureFormat.RGBA32, false);
            m_Colors = new Color32[32 * c_TextureHeight];
        }

#if (UNITY_EDITOR)
        [Button]
        private void Randomize()
        {
            Initialize();
            RandomizeBoost();
        }
#endif

        public int GetBoost(int lane, int i)
        {
            return m_BoostValues[lane, (i + m_TrackLength) % m_TrackLength];
        }

        public void RandomizeBoost()
        {
            Array.Clear(m_Colors, 0, m_Colors.Length);
            Array.Clear(m_BoostValues, 0, m_BoostValues.Length);

            int[,] xPix = GetXPixels();

            for (int iLane = 0; iLane < NumLanes; iLane++)
            {
                int iTrack = 0;
                while (iTrack < m_TrackLength)
                {
                    iTrack += Random.Range(m_MinSpacing, m_MaxSpacing + 1);
                    if (iTrack >= m_TrackLength - m_MinLength)
                    {
                        // Don't wrap.
                        break;
                    }

                    bool isPositive = Random.value < m_Ratio;
                    int iMax = iTrack + Random.Range(m_MinLength, m_MaxLength + 1);
                    iMax = Mathf.Min(iMax, m_TrackLength - 1);

                    // Draw with 1px gaps.
                    Color col = isPositive ? m_Positive : m_Negative;
                    int yMin = GetYPixel(iTrack);
                    int yMax = GetYPixel(iMax);

                    for (int y = yMin; y < yMax; y += 2)
                    {
                        for (int x = xPix[iLane, 0]; x < xPix[iLane, 5]; x++)
                        {
                            m_Colors[y * c_TextureWidth + x] = col;
                        }
                    }

                    // Set values, no gaps.
                    int value = isPositive ? 1 : -1;

                    for (; iTrack < iMax; iTrack++)
                    {
                        m_BoostValues[iLane, iTrack] = value;
                    }
                }
            }

            m_Texture.SetPixels32(m_Colors);
            m_Texture.Apply();
            m_Material.mainTexture = m_Texture;
            m_Material.SetTexture("_EmissionMap", m_Texture);
        }

        private int GetYPixel(int i)
        {
            return Mathf.Clamp(Mathf.FloorToInt(
                m_Track.GetPoint(i).CumlLengthRatio * c_TextureHeight) - m_DrawOffset,
                0, c_TextureHeight);
        }

        // 4 lanes, each is 6px wide.
        // TODO Generalize.
        private int[,] GetXPixels()
        {
            return new int[,]
            {
                { 3, 4, 5, 6, 7, 8 },
                { 10, 11, 12, 13, 14, 15 },
                { 17, 18, 19, 20, 21, 22 },
                { 24, 25, 26, 27, 28, 29 }
            };
        }

        //private void OnDrawGizmosSelected()
        //{
        //    if (m_BoostValues != null)
        //    {
        //        for (int i = 0; i < m_Track.Length; i++)
        //        {
        //            var p = m_Track.GetPoint(i);
        //            for (int lane = 0; lane < NumLanes; lane++)
        //            {
        //                if (m_BoostValues[lane, i] != 0)
        //                {
        //                    float x = lane * 2 - 3;
        //                    Gizmos.color = m_BoostValues[lane, i] > 0 ? m_Positive : m_Negative;
        //                    Gizmos.DrawSphere(p.Position + p.Right * -x, 0.25f);
        //                }
        //            }
        //        }
        //    }
        //}
    }
}