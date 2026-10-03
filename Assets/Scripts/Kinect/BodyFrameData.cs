using System;

namespace Naispo.Kinect
{
    // Plain CPU data: never holds an SDK frame/image or a UnityEngine.Object.
    public sealed class BodyFrameData
    {
        public int Width, Height, BodyCount;
        public long Sequence;
        public double TimestampSeconds;
        public byte[] BodyIndexMap = Array.Empty<byte>();
        public readonly uint[] BodyIds = new uint[255];

        public void EnsureSize(int width, int height)
        {
            if (Width == width && Height == height) return;
            Width = width;
            Height = height;
            BodyIndexMap = new byte[checked(width * height)];
        }
    }

    // Three owners: producer, pending latest frame, consumer. No unbounded queue,
    // no copies under the lock, and producer can never overwrite the consumer.
    public sealed class LatestBodyFrameBuffer
    {
        readonly object gate = new object();
        BodyFrameData pending = new BodyFrameData();
        bool available;

        public void Publish(ref BodyFrameData producer)
        {
            lock (gate)
            {
                var recycle = pending;
                pending = producer;
                producer = recycle;
                available = true;
            }
        }

        public bool TryTake(ref BodyFrameData consumer)
        {
            lock (gate)
            {
                if (!available) return false;
                var recycle = consumer;
                consumer = pending;
                pending = recycle;
                available = false;
                return true;
            }
        }
    }
}
