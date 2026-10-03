using Naispo.Kinect;

namespace Naispo.Silhouette
{
    public sealed class BodyColorRegistry
    {
        public struct Slot
        {
            public uint BodyId;
            public bool Assigned, Present;
            public double LastSeen;
        }
        public readonly Slot[] Slots = new Slot[3];
        public readonly int[] BodyIndexToSlot = new int[256];
        public int PresentCount { get; private set; }

        public void Update(BodyFrameData frame, double now, int maximumBodies, double lostTimeout)
        {
            maximumBodies = System.Math.Max(1, System.Math.Min(3, maximumBodies));
            PresentCount = 0;
            for (int i = 0; i < BodyIndexToSlot.Length; i++) BodyIndexToSlot[i] = -1;
            // Match existing IDs BEFORE expiring slots. Index ordering may change every frame.
            for (int slot = 0; slot < 3; slot++)
            {
                var value = Slots[slot];
                value.Present = false;
                if (slot >= maximumBodies) value.Assigned = false;
                if (value.Assigned && frame != null)
                    for (int i = 0; i < frame.BodyCount; i++)
                        if (frame.BodyIds[i] == value.BodyId)
                        { value.Present = true; value.LastSeen = now; BodyIndexToSlot[i] = slot; break; }
                if (!value.Present && now - value.LastSeen >= lostTimeout) value.Assigned = false;
                Slots[slot] = value;
                if (value.Present) PresentCount++;
            }
            if (frame == null) return;
            for (int i = 0; i < frame.BodyCount; i++)
            {
                if (BodyIndexToSlot[i] >= 0) continue;
                for (int slot = 0; slot < maximumBodies; slot++)
                {
                    if (Slots[slot].Assigned) continue;
                    Slots[slot] = new Slot { Assigned = true, Present = true, BodyId = frame.BodyIds[i], LastSeen = now };
                    BodyIndexToSlot[i] = slot;
                    PresentCount++;
                    break;
                }
            }
        }
    }
}
