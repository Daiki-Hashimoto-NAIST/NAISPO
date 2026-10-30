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
            Assert.That(match.TryCollectSpell(RallySpell.Fireball), Is.False);
            Assert.That(match.ReleaseSpellForRebound(-losingSide), Is.EqualTo(RallySpell.None));
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.None));
        }

        [Test]
        public void PlayerWhoMissedReceivesNextServeAndPointClearsActiveAndBothHeldSpells()
        {
            var match = MatchWithActiveAndBothHeldSpells();
            match.AwardPoint(1);
            Assert.That(match.NextServeSide, Is.EqualTo(1));
            Assert.That(match.LeftScore, Is.EqualTo(1));
            AssertNoRallyEffects(match);
            match.BeginRally();
            match.AwardPoint(-1);
            Assert.That(match.NextServeSide, Is.EqualTo(-1));
            Assert.That(match.RightScore, Is.EqualTo(1));
        }

        [TestCase(-1, RallySpell.Fireball)]
        [TestCase(-1, RallySpell.Timer)]
        [TestCase(-1, RallySpell.Light)]
        [TestCase(1, RallySpell.Fireball)]
        [TestCase(1, RallySpell.Timer)]
        [TestCase(1, RallySpell.Light)]
        public void PickupWaitsForOwnersReturnAndReleasesOnlyOnce(int owner, RallySpell spell)
        {
            var match = new RallyVersusState();
            match.BeginRally();
            Assert.That(match.TryCollectSpell(spell), Is.False,
                "A serve has no pickup owner before a player returns the ball.");
            match.RegisterBarrierHit(owner);
            Assert.That(match.TryCollectSpell(spell), Is.True);
            Assert.That(match.PendingSpell(owner), Is.EqualTo(spell));
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.None));
            Assert.That(match.SpellOwner, Is.Zero);
            Assert.That(match.SpellSecondsRemaining, Is.Zero);

            match.StepSpell(60f, false);
            Assert.That(match.PendingSpell(owner), Is.EqualTo(spell), "Held spells have no timeout.");
            match.RegisterBarrierHit(-owner);
            Assert.That(match.ReleaseSpellForRebound(owner), Is.EqualTo(RallySpell.None),
                "An owner can only release a spell when they are the current hitter.");
            Assert.That(match.ReleaseSpellForRebound(-owner), Is.EqualTo(RallySpell.None));
            Assert.That(match.PendingSpell(owner), Is.EqualTo(spell), "The opponent's return cannot consume it.");

            match.RegisterBarrierHit(owner);
            Assert.That(match.ReleaseSpellForRebound(owner), Is.EqualTo(spell));
            Assert.That(match.PendingSpell(owner), Is.EqualTo(RallySpell.None));
            Assert.That(match.ActiveSpell, Is.EqualTo(spell));
            Assert.That(match.SpellOwner, Is.EqualTo(owner));
            float lifetime = spell == RallySpell.Light ? 0 : RallyVersusState.SpellLifetimeSeconds;
            Assert.That(match.SpellSecondsRemaining, Is.EqualTo(lifetime).Within(.001f));
            Assert.That(match.ReleaseSpellForRebound(owner), Is.EqualTo(RallySpell.None));
            Assert.That(match.ActiveSpell, Is.EqualTo(spell), "A duplicate release cannot cancel or reapply the shot.");
            Assert.That(match.SpellSecondsRemaining, Is.EqualTo(lifetime).Within(.001f));
        }

        [Test]
        public void EachPlayerKeepsOneHeldSpellAndOnlyTheirLatestPickupReplacesIt()
        {
            var match = new RallyVersusState();
            match.BeginRally();
            match.RegisterBarrierHit(-1);
            match.TryCollectSpell(RallySpell.Fireball);
            match.TryCollectSpell(RallySpell.Fireball);
            match.RegisterBarrierHit(1);
            match.TryCollectSpell(RallySpell.Timer);
            Assert.That(match.PendingSpell(-1), Is.EqualTo(RallySpell.Fireball));
            Assert.That(match.PendingSpell(1), Is.EqualTo(RallySpell.Timer));
            match.TryCollectSpell(RallySpell.Light);
            Assert.That(match.PendingSpell(-1), Is.EqualTo(RallySpell.Fireball));
            Assert.That(match.PendingSpell(1), Is.EqualTo(RallySpell.Light));
            match.RegisterBarrierHit(-1);
            Assert.That(match.ReleaseSpellForRebound(-1), Is.EqualTo(RallySpell.Fireball));
            Assert.That(match.PendingSpell(1), Is.EqualTo(RallySpell.Light));
            match.RegisterBarrierHit(1);
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.None), "Contact ends the previous enchanted flight.");
            Assert.That(match.SpellOwner, Is.Zero);
            Assert.That(match.PendingSpell(1), Is.EqualTo(RallySpell.Light));
            Assert.That(match.ReleaseSpellForRebound(1), Is.EqualTo(RallySpell.Light));
            Assert.That(match.SpellOwner, Is.EqualTo(1));
        }

        [TestCase(RallySpell.Fireball)]
        [TestCase(RallySpell.Timer)]
        public void ActiveFlightExpiresWithoutExpiringHeldSpells(RallySpell spell)
        {
            var match = new RallyVersusState();
            match.BeginRally();
            match.RegisterBarrierHit(-1);
            match.TryCollectSpell(RallySpell.Light);
            match.RegisterBarrierHit(1);
            match.TryCollectSpell(spell);
            match.ReleaseSpellForRebound(1);
            match.StepSpell(3f, false);
            Assert.That(match.SpellSecondsRemaining, Is.EqualTo(3f).Within(.001f));
            match.TryCollectSpell(RallySpell.Timer);
            Assert.That(match.SpellSecondsRemaining, Is.EqualTo(3f).Within(.001f),
                "Collecting another spell cannot restart the active shot or its lifetime.");
            Assert.That(match.ActiveSpell, Is.EqualTo(spell));
            match.StepSpell(RallyVersusState.SpellLifetimeSeconds, false);
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.None));
            Assert.That(match.SpellOwner, Is.Zero);
            Assert.That(match.SpellSecondsRemaining, Is.Zero);
            Assert.That(match.PendingSpell(-1), Is.EqualTo(RallySpell.Light));
            Assert.That(match.PendingSpell(1), Is.EqualTo(RallySpell.Timer));
        }

        [Test]
        public void CompressionFreezesActiveSpellLifetime()
        {
            var match = new RallyVersusState();
            match.BeginRally();
            match.RegisterBarrierHit(-1);
            match.TryCollectSpell(RallySpell.Timer);
            match.ReleaseSpellForRebound(-1);
            match.StepSpell(4f, true);
            Assert.That(match.SpellSecondsRemaining, Is.EqualTo(RallyVersusState.SpellLifetimeSeconds).Within(.001f),
                "Barrier compression must not spend the active-flight lifetime.");
            match.StepSpell(4f, false);
            Assert.That(match.SpellSecondsRemaining, Is.EqualTo(2f).Within(.001f));
        }

        [Test]
        public void LightHasNoReboundMultiplierAndItsPhaseIsClearedSeparatelyFromHeldSpells()
        {
            var match = new RallyVersusState();
            match.BeginRally();
            match.RegisterBarrierHit(1);
            match.TryCollectSpell(RallySpell.Light);
            Assert.That(match.ReleaseSpellForRebound(1), Is.EqualTo(RallySpell.Light));
            Assert.That(RallyVersusState.SpellMultiplier(RallySpell.Light), Is.EqualTo(1f));
            match.TryCollectSpell(RallySpell.Fireball);
            match.StepSpell(60f, false);
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.Light),
                "Light's warning and hidden phases are timed by the game.");
            match.ClearSpell();
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.None));
            Assert.That(match.SpellOwner, Is.Zero);
            Assert.That(match.PendingSpell(1), Is.EqualTo(RallySpell.Fireball));
        }

        [TestCase(0)]
        [TestCase(-2)]
        [TestCase(2)]
        public void InvalidSidesCannotChangeOwnerSpellScoreOrRally(int invalidSide)
        {
            var match = MatchWithActiveAndBothHeldSpells();
            match.RegisterBarrierHit(invalidSide);
            Assert.That(match.ReleaseSpellForRebound(invalidSide), Is.EqualTo(RallySpell.None));
            Assert.That(match.PendingSpell(invalidSide), Is.EqualTo(RallySpell.None));
            Assert.That(match.AwardPoint(invalidSide), Is.False);
            Assert.That(match.LastHitter, Is.EqualTo(1));
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.Fireball));
            Assert.That(match.SpellOwner, Is.EqualTo(1));
            Assert.That(match.PendingSpell(-1), Is.EqualTo(RallySpell.Light));
            Assert.That(match.PendingSpell(1), Is.EqualTo(RallySpell.Timer));
            Assert.That(match.LeftScore, Is.Zero);
            Assert.That(match.RightScore, Is.Zero);
            Assert.That(match.NextServeSide, Is.EqualTo(-1));
            Assert.That(match.RallyActive, Is.True);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(4)]
        public void InvalidSpellCannotReplaceHeldOrActiveSpells(int invalidSpell)
        {
            var match = MatchWithActiveAndBothHeldSpells();
            Assert.That(match.TryCollectSpell((RallySpell)invalidSpell), Is.False);
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.Fireball));
            Assert.That(match.SpellOwner, Is.EqualTo(1));
            Assert.That(match.PendingSpell(-1), Is.EqualTo(RallySpell.Light));
            Assert.That(match.PendingSpell(1), Is.EqualTo(RallySpell.Timer));
        }

        [Test]
        public void WaitingForServeRejectsHitsSpellsReleasesAndPoints()
        {
            var match = new RallyVersusState();
            match.RegisterBarrierHit(1);
            Assert.That(match.LastHitter, Is.Zero);
            Assert.That(match.TryCollectSpell(RallySpell.Light), Is.False);
            Assert.That(match.ReleaseSpellForRebound(1), Is.EqualTo(RallySpell.None));
            Assert.That(match.AwardPoint(-1), Is.False);
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.None));
            Assert.That(match.RightScore, Is.Zero);
        }

        [Test]
        public void NewRallyClearsActiveAndBothHeldSpells()
        {
            var match = MatchWithActiveAndBothHeldSpells();
            match.BeginRally();
            Assert.That(match.RallyActive, Is.True);
            AssertNoRallyEffects(match);
        }

        [Test]
        public void ResetClearsActiveAndBothHeldSpellsAndFinishedMatch()
        {
            var match = MatchWithActiveAndBothHeldSpells();
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

        static RallyVersusState MatchWithActiveAndBothHeldSpells()
        {
            var match = new RallyVersusState();
            match.BeginRally();
            match.RegisterBarrierHit(-1);
            match.TryCollectSpell(RallySpell.Light);
            match.RegisterBarrierHit(1);
            match.TryCollectSpell(RallySpell.Fireball);
            match.ReleaseSpellForRebound(1);
            match.TryCollectSpell(RallySpell.Timer);
            return match;
        }

        static void AssertNoRallyEffects(RallyVersusState match)
        {
            Assert.That(match.LastHitter, Is.Zero);
            Assert.That(match.ActiveSpell, Is.EqualTo(RallySpell.None));
            Assert.That(match.SpellOwner, Is.Zero);
            Assert.That(match.SpellSecondsRemaining, Is.Zero);
            Assert.That(match.PendingSpell(-1), Is.EqualTo(RallySpell.None));
            Assert.That(match.PendingSpell(1), Is.EqualTo(RallySpell.None));
        }

        static void AssertResetState(RallyVersusState match)
        {
            Assert.That(match.LeftScore, Is.Zero);
            Assert.That(match.RightScore, Is.Zero);
            AssertNoRallyEffects(match);
            Assert.That(match.Winner, Is.Zero);
            Assert.That(match.Complete, Is.False);
            Assert.That(match.RallyActive, Is.False);
            Assert.That(match.NextServeSide, Is.EqualTo(-1));
        }
    }
}
