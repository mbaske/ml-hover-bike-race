using System.Collections.Generic;
using UnityEngine;

namespace Hoverbike.Track
{
    public struct Lane
    {
        public float min;
        public float max;
        public int minInt;
        public int maxInt;
        public int index;

        public bool Contains(float val)
        {
            return val >= min && val <= max;
        }
    }

    public class Lanes
    {
        public int Count { get; private set; }

        private List<Lane> lanes;

        public Lanes(List<Lane> lanes)
        {
            this.lanes = lanes;
            Count = lanes.Count;
        }

        public Lanes(int count, float extent, float spacing)
        {
            Count = count;
            int nSpaces = count - 1;
            float total = extent * 2f;
            float width = total / (float)(count + nSpaces);
            float space = nSpaces * width * spacing;
            width = (total - space) / (float)(count);
            space /= (float)nSpaces;

            float min = -extent;
            lanes = new List<Lane>(count);
            for (int i = 0; i < count; i++)
            {
                // Normalized -1/+1.
                lanes.Add(new Lane()
                {
                    min = min,
                    max = min + width,
                    index = i
                });
                min += width + space;
            }
        }

        public Lane this[int i]
        {
            get { return lanes[i]; }
        }

        public IEnumerator<Lane> GetEnumerator()
        {
            return lanes.GetEnumerator();
        }

        public bool Contains(float val, out int index)
        {
            foreach (Lane lane in lanes)
            {
                if (lane.Contains(val))
                {
                    index = lane.index;
                    return true;
                }
            }
            index = -1;
            return false;
        }

        public Lanes Scale(float scale)
        {
            var scaled = new List<Lane>(Count);
            for (int i = 0; i < Count; i++)
            {
                scaled.Add(new Lane()
                {
                    min = lanes[i].min * scale,
                    max = lanes[i].max * scale,
                    index = lanes[i].index
                });
            }
            return new Lanes(scaled);
        }

        public Lanes ScaleInt(float scale, int offset)
        {
            var scaled = new List<Lane>(Count);
            for (int i = 0; i < Count; i++)
            {
                scaled.Add(new Lane()
                {
                    minInt = Mathf.RoundToInt(lanes[i].min * scale) + offset,
                    maxInt = Mathf.RoundToInt(lanes[i].max * scale) + offset,
                    index = lanes[i].index
                });
            }
            return new Lanes(scaled);
        }
    }
}