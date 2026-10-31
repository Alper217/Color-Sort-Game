using System;

namespace SandSort.Data
{
    /// <summary>
    /// Immutable value-type definition of a single color container bucket.
    /// </summary>
    [Serializable]
    public struct BucketData : IEquatable<BucketData>
    {
        public int ColorId;
        public int MaxCapacity;

        public BucketData(int colorId, int maxCapacity)
        {
            ColorId = colorId;
            MaxCapacity = maxCapacity;
        }

        public bool IsValid => ColorId >= 0 && MaxCapacity > 0;

        public bool Equals(BucketData other)
        {
            return ColorId == other.ColorId && MaxCapacity == other.MaxCapacity;
        }

        public override bool Equals(object obj)
        {
            return obj is BucketData other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (ColorId * 397) ^ MaxCapacity;
        }

        public override string ToString()
        {
            return $"Bucket(Color:{ColorId}, Cap:{MaxCapacity})";
        }
    }

    /// <summary>
    /// Serializable wrapper for a single spawner column queue of buckets.
    /// Index 0 represents the front/top bucket ready to be clicked first.
    /// </summary>
    [Serializable]
    public struct BucketColumnData
    {
        public BucketData[] Buckets;

        public int Count => Buckets != null ? Buckets.Length : 0;
    }
}

