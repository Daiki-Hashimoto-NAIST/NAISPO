using UnityEngine;

namespace Naispo.Interaction
{
    public readonly struct RallyPickupSnapshot
    {
        public readonly RallySpell Spell;
        public readonly Vector2 Position;
        public readonly float Age;
        public readonly bool Visible;
        public readonly bool Collectible;

        public RallyPickupSnapshot(RallySpell spell, Vector2 position, float age, bool visible, bool collectible)
        {
            Spell = spell;
            Position = position;
            Age = age;
            Visible = visible;
            Collectible = collectible;
        }
    }

    /// <summary>A deterministic shuffled bag that keeps gameplay's random stream untouched.</summary>
    public sealed class RallyPickupBag
    {
        static readonly RallySpell[] Types = { RallySpell.Fireball, RallySpell.Timer, RallySpell.Light };
        readonly int[] order = new int[3];
        readonly System.Random random;
        int next;

        public RallyPickupBag(int seed)
        {
            random = new System.Random(seed);
            Refill();
        }

        public RallySpell Next()
        {
            if (next >= order.Length) Refill();
            return Types[order[next++]];
        }

        public void Reset() => Refill();

        void Refill()
        {
            for (int i = 0; i < order.Length; i++) order[i] = i;
            for (int i = order.Length - 1; i > 0; i--)
            {
                int swap = random.Next(i + 1);
                int value = order[i];
                order[i] = order[swap];
                order[swap] = value;
            }
            next = 0;
        }
    }
}
