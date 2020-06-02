using System.Collections.Generic;
using UnityEngine;

namespace Hoverbike.Track
{
    public class SearchableLocations<T> where T : TrackLocation
    {
        public int Count => list.Count;
        private List<T> list;
        private int searchIncr;

        public SearchableLocations(int size)
        {
            list = new List<T>(size);
            searchIncr = (int)Mathf.Sqrt(size); // TBD
        }

        public T this[int i]
        {
            get { return list[(i + Count) % Count]; }
        }

        public IEnumerator<T> GetEnumerator()
        {
            return list.GetEnumerator();
        }

        public void Add(T loc)
        {
            loc.Index = Count;
            list.Add(loc);
        }

        public T AtDegree(float deg)
        {
            return AtNormalizedT(deg / 360f);
        }

        public T AtNormalizedT(float t)
        {
            return this[Mathf.RoundToInt(t * Count)];
        }

        // Can't use C# List<T>.BinarySearch over the entire list, because nearby locations
        // aren't necessarily neighbouring locations on a track that curves in on itself.
        // We need to constrain the min/max indices first. Absent a seed (a known previous
        // location), we run a preliminary, low resolution incremental search over the whole 
        // track in order to narrow down the search space.

        public T FindClosest(Vector3 pV3)
        {
            return this[BinarySearch(pV3, IncrementalSearch(pV3))];
        }

        public T FindClosest(Vector3 pV3, T seed)
        {
            float sm = (pV3 - seed.PosV3).sqrMagnitude;
            if (sm < (pV3 - this[seed.Index - 1].PosV3).sqrMagnitude && 
                sm < (pV3 - this[seed.Index + 1].PosV3).sqrMagnitude)
            {
                // Hasn't moved to new location -> can skip search.
                return seed;
            }

            return this[BinarySearch(pV3, seed.Index)];
        }

        private int IncrementalSearch(Vector3 pV3)
        {
            int index = 0;
            float min = Mathf.Infinity;
            for (int i = 0; i < Count; i += searchIncr)
            {
                float d = (pV3 - this[i].PosV3).sqrMagnitude;
                if (d < min)
                {
                    min = d;
                    index = i;
                }
            }
            return index;
        }

        private int BinarySearch(Vector3 pV3, int seed)
        {
            return BinarySearch(pV3, seed - searchIncr, seed + searchIncr);
        }

        private int BinarySearch(Vector3 pV3, int min, int max)
        {
            bool b = (pV3 - this[min].PosV3).sqrMagnitude < (pV3 - this[max].PosV3).sqrMagnitude;
            int delta = max - min, mid = min + delta / 2;
            return delta == 1 ? (b ? min : max) : BinarySearch(pV3, b ? min : mid, b ? mid : max);
        }

        public void Draw()
        {
            foreach (TrackLocation loc in list)
            {
                loc.Draw();
            }
        }
    }
}