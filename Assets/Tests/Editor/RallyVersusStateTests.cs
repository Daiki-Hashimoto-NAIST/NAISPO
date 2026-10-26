using NUnit.Framework;
using Naispo.Interaction;

namespace Naispo.Tests
{
    public sealed class RallyVersusStateTests
    {
        [TestCase(-1)]
        [TestCase(1)]
        public void ThreePointsWinAndEachRallyCanScoreOnlyOnce(int losingSide)
        {
            var match = new RallyVersusState();
            for (int point = 1; point <= RallyVersusState.PointsToWin; point++)
            {
                match.BeginRally();
                Assert.That(match.AwardPoint(losingSide), Is.True);
                Assert.That(match.RallyActive, Is.False);
                Assert.That(match.AwardPoint(losingSide), Is.False);
                Assert.That(match.AwardPoint(-losingSide), Is.False);
                Assert.That(losingSide < 0 ? match.RightScore : match.LeftScore, Is.EqualTo(point));
                Assert.That(losingSide < 0 ? match.LeftScore : match.RightScore, Is.Zero);
                Assert.That(match.Complete, Is.EqualTo(point == RallyVersusState.PointsToWin));
            }
            Assert.That(match.Winner, Is.EqualTo(-losingSide));
            match.BeginRally();
            Assert.That(match.RallyActive, Is.False);
            Assert.That(match.AwardPoint(losingSide), Is.False);
            match.RegisterBarrierHit(-losingSide);
            Assert.That(match.LastHitter, Is.Zero);
            Assert.That(match.TryActivateSpell(RallySpell.Fireball), Is.False);
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.None));
        }

        [Test]
        public void PlayerWhoMissedReceivesNextServeAndPointClearsSpellAndOwner()
        {
            var match = new RallyVersusState();
            Assert.That(match.NextServeSide, Is.EqualTo(-1));
            match.BeginRally();
            match.RegisterBarrierHit(-1);
            Assert.That(match.TryActivateSpell(RallySpell.Fireball), Is.True);
            match.AwardPoint(1);
            Assert.That(match.NextServeSide, Is.EqualTo(1));
            Assert.That(match.LeftScore, Is.EqualTo(1));
            Assert.That(match.LastHitter, Is.Zero);
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.None));
            Assert.That(match.SpellOwner, Is.Zero);

            match.BeginRally();
            match.AwardPoint(-1);
            Assert.That(match.NextServeSide, Is.EqualTo(-1));
            Assert.That(match.RightScore, Is.EqualTo(1));
        }

        [Test]
        public void FirstBarrierContactOwnsThePickupAndDifferentSpellReplacesIt()
        {
            var match = new RallyVersusState();
            match.BeginRally();
            Assert.That(match.TryActivateSpell(RallySpell.Fireball), Is.False,
                "A serve has no pickup owner before a player returns the ball.");
            match.RegisterBarrierHit(-1);
            Assert.That(match.TryActivateSpell(RallySpell.Fireball), Is.True);
            Assert.That(match.SpellOwner, Is.EqualTo(-1));
            Assert.That(match.SpellSecondsRemaining, Is.EqualTo(RallyVersusState.SpellLifetimeSeconds).Within(.001f));

            match.RegisterBarrierHit(1);
            Assert.That(match.TryActivateSpell(RallySpell.Timer), Is.True);
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.Timer));
            Assert.That(match.SpellOwner, Is.EqualTo(1));
            Assert.That(match.SpellSecondsRemaining, Is.EqualTo(RallyVersusState.SpellLifetimeSeconds).Within(.001f));
            Assert.That(match.ConsumeSpellMultiplier(), Is.EqualTo(RallyVersusState.TimerMultiplier).Within(.001f));
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.None), "A single return consumes the one pending spell.");
        }

        [Test]
        public void IdenticalPickupRefreshesWithoutStackingAndFlightExpiryClears()
        {
            var match = new RallyVersusState();
            match.BeginRally();
            match.RegisterBarrierHit(-1);
            match.TryActivateSpell(RallySpell.Fireball);
            match.StepSpell(3f, false);
            Assert.That(match.SpellSecondsRemaining, Is.EqualTo(3f).Within(.001f));
            match.TryActivateSpell(RallySpell.Fireball);
            Assert.That(match.SpellSecondsRemaining, Is.EqualTo(RallyVersusState.SpellLifetimeSeconds).Within(.001f),
                "Repeated identical pickups refresh rather than accumulate multipliers.");
            Assert.That(match.ConsumeSpellMultiplier(), Is.EqualTo(RallyVersusState.FireballMultiplier).Within(.001f));
            Assert.That(match.TryActivateSpell(RallySpell.Timer), Is.True);
            match.StepSpell(RallyVersusState.SpellLifetimeSeconds + .1f, false);
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.None));
            Assert.That(match.SpellOwner, Is.Zero);
        }

        [Test]
        public void CompressionFreezesSpellLifetime()
        {
            var match = new RallyVersusState();
            match.BeginRally();
            match.RegisterBarrierHit(-1);
            match.TryActivateSpell(RallySpell.Timer);
            match.StepSpell(4f, true);
            Assert.That(match.SpellSecondsRemaining, Is.EqualTo(RallyVersusState.SpellLifetimeSeconds).Within(.001f),
                "Barrier compression must not spend the active-flight lifetime.");
            match.StepSpell(4f, false);
            Assert.That(match.SpellSecondsRemaining, Is.EqualTo(2f).Within(.001f));
        }

        [Test]
        public void LightHasNoReboundMultiplierAndIsNotConsumedByAReturn()
        {
            var match = new RallyVersusState();
            match.BeginRally();
            match.RegisterBarrierHit(1);
            Assert.That(match.TryActivateSpell(RallySpell.Light), Is.True);
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.Light));
            Assert.That(match.SpellOwner, Is.EqualTo(1));
            Assert.That(match.ConsumeSpellMultiplier(), Is.EqualTo(1f));
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.Light),
                "Light owns a visible phase, not a physics multiplier.");
        }

        [TestCase(0)]
        [TestCase(-2)]
        [TestCase(2)]
        public void InvalidSidesCannotChangeOwnerSpellScoreOrRally(int invalidSide)
        {
            var match = new RallyVersusState();
            match.BeginRally();
            match.RegisterBarrierHit(-1);
            match.TryActivateSpell(RallySpell.Fireball);
            match.RegisterBarrierHit(invalidSide);
            Assert.That(match.AwardPoint(invalidSide), Is.False);
            Assert.That(match.LastHitter, Is.EqualTo(-1));
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.Fireball));
            Assert.That(match.SpellOwner, Is.EqualTo(-1));
            Assert.That(match.LeftScore, Is.Zero);
            Assert.That(match.RightScore, Is.Zero);
            Assert.That(match.NextServeSide, Is.EqualTo(-1));
            Assert.That(match.RallyActive, Is.True);
        }

        [Test]
        public void WaitingForServeRejectsHitsSpellsAndPoints()
        {
            var match = new RallyVersusState();
            match.RegisterBarrierHit(1);
            Assert.That(match.LastHitter, Is.Zero);
            Assert.That(match.TryActivateSpell(RallySpell.Light), Is.False);
            Assert.That(match.AwardPoint(-1), Is.False);
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.None));
            Assert.That(match.RightScore, Is.Zero);
        }

        [Test]
        public void ResetClearsInProgressSpellAndFinishedMatch()
        {
            var match = new RallyVersusState();
            match.BeginRally();
            match.RegisterBarrierHit(-1);
            match.TryActivateSpell(RallySpell.Light);
            match.Reset();
            AssertResetState(match);

            for (int point = 0; point < RallyVersusState.PointsToWin; point++)
            {
                match.BeginRally();
                match.AwardPoint(1);
            }
            Assert.That(match.Complete, Is.True);
            match.Reset();
            AssertResetState(match);
            match.BeginRally();
            Assert.That(match.RallyActive, Is.True);
        }

        static void AssertResetState(RallyVersusState match)
        {
            Assert.That(match.LeftScore, Is.Zero);
            Assert.That(match.RightScore, Is.Zero);
            Assert.That(match.LastHitter, Is.Zero);
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.None));
            Assert.That(match.SpellOwner, Is.Zero);
            Assert.That(match.SpellSecondsRemaining, Is.Zero);
            Assert.That(match.Winner, Is.Zero);
            Assert.That(match.Complete, Is.False);
            Assert.That(match.RallyActive, Is.False);
            Assert.That(match.NextServeSide, Is.EqualTo(-1));
        }
    }
}
