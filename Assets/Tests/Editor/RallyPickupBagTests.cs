using System.Collections.Generic;
using NUnit.Framework;
using Naispo.Interaction;

namespace Naispo.Tests
{
    public sealed class RallyPickupBagTests
    {
        [Test]
        public void EveryBagOfThreeCoversAllSpellTypes()
        {
            var bag = new RallyPickupBag(12345);
            for (int group = 0; group < 40; group++)
            {
                var seen = new HashSet<RallySpell>();
                for (int draw = 0; draw < 3; draw++) seen.Add(bag.Next());
                Assert.That(seen.Count, Is.EqualTo(3), "Each shuffled bag must contain one of every spell.");
                Assert.That(seen.Contains(RallySpell.Fireball), Is.True);
                Assert.That(seen.Contains(RallySpell.Timer), Is.True);
                Assert.That(seen.Contains(RallySpell.Light), Is.True);
            }
        }

        [Test]
        public void SameSeedProducesTheSameOrderAndResetStartsAFreshBag()
        {
            var first = new RallyPickupBag(77);
            var second = new RallyPickupBag(77);
            for (int draw = 0; draw < 9; draw++)
                Assert.That(second.Next(), Is.EqualTo(first.Next()), "The bag must use its own deterministic seeded stream.");

            first.Reset();
            var reset = new HashSet<RallySpell>();
            for (int draw = 0; draw < 3; draw++) reset.Add(first.Next());
            Assert.That(reset.Count, Is.EqualTo(3), "Reset must begin a fresh shuffled bag rather than draining an old one.");
        }
    }
}
