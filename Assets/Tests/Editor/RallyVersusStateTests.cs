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
            Assert.That(match.RegisterReturn(-losingSide), Is.EqualTo(1f));
            Assert.That(match.CollectItem(), Is.False);
            Assert.That(match.LastHitter, Is.Zero);
        }

        [Test]
        public void PlayerWhoMissedReceivesNextServeAndPointClearsEffects()
        {
            var match = new RallyVersusState();
            Assert.That(match.NextServeSide, Is.EqualTo(-1));
            match.BeginRally();
            match.RegisterReturn(-1);
            match.CollectItem();
            match.AwardPoint(1);
            Assert.That(match.NextServeSide, Is.EqualTo(1));
            Assert.That(match.LeftScore, Is.EqualTo(1));
            Assert.That(match.LastHitter, Is.Zero);
            Assert.That(match.SoftenedSide, Is.Zero);

            match.BeginRally();
            match.AwardPoint(-1);
            Assert.That(match.NextServeSide, Is.EqualTo(-1));
            Assert.That(match.RightScore, Is.EqualTo(1));
        }

        [TestCase(-1)]
        [TestCase(1)]
        public void LastReturnAttributesItemAndOnlyOpponentsNextReturnIsSoftened(int collector)
        {
            var match = new RallyVersusState();
            match.BeginRally();
            Assert.That(match.CollectItem(), Is.False, "A serve has no item owner before a player returns it.");
            match.RegisterReturn(-collector);
            match.RegisterReturn(collector);
            Assert.That(match.LastHitter, Is.EqualTo(collector));
            Assert.That(match.CollectItem(), Is.True);
            Assert.That(match.SoftenedSide, Is.EqualTo(-collector));

            Assert.That(match.RegisterReturn(collector), Is.EqualTo(1f));
            Assert.That(match.SoftenedSide, Is.EqualTo(-collector));
            Assert.That(match.RegisterReturn(-collector), Is.EqualTo(RallyVersusState.SoftReturnMultiplier));
            Assert.That(match.LastHitter, Is.EqualTo(-collector));
            Assert.That(match.SoftenedSide, Is.Zero);
            Assert.That(match.RegisterReturn(-collector), Is.EqualTo(1f));
        }

        [Test]
        public void RepeatedItemsDoNotStackOrExtendBeyondOneReturn()
        {
            var match = new RallyVersusState();
            match.BeginRally();
            match.RegisterReturn(-1);
            Assert.That(match.CollectItem(), Is.True);
            Assert.That(match.CollectItem(), Is.True);
            Assert.That(match.RegisterReturn(1), Is.EqualTo(RallyVersusState.SoftReturnMultiplier));
            Assert.That(match.RegisterReturn(1), Is.EqualTo(1f));
            Assert.That(match.CollectItem(), Is.True);
            Assert.That(match.SoftenedSide, Is.EqualTo(-1));
        }

        [TestCase(0)]
        [TestCase(-2)]
        [TestCase(2)]
        public void InvalidSidesCannotChangeOwnerEffectScoreOrRally(int invalidSide)
        {
            var match = new RallyVersusState();
            match.BeginRally();
            match.RegisterReturn(-1);
            match.CollectItem();
            Assert.That(match.RegisterReturn(invalidSide), Is.EqualTo(1f));
            Assert.That(match.AwardPoint(invalidSide), Is.False);
            Assert.That(match.LastHitter, Is.EqualTo(-1));
            Assert.That(match.SoftenedSide, Is.EqualTo(1));
            Assert.That(match.LeftScore, Is.Zero);
            Assert.That(match.RightScore, Is.Zero);
            Assert.That(match.NextServeSide, Is.EqualTo(-1));
            Assert.That(match.RallyActive, Is.True);
        }

        [Test]
        public void WaitingForServeRejectsReturnsItemsAndPoints()
        {
            var match = new RallyVersusState();
            Assert.That(match.RegisterReturn(1), Is.EqualTo(1f));
            Assert.That(match.CollectItem(), Is.False);
            Assert.That(match.AwardPoint(-1), Is.False);
            Assert.That(match.LastHitter, Is.Zero);
            Assert.That(match.SoftenedSide, Is.Zero);
            Assert.That(match.RightScore, Is.Zero);
        }

        [Test]
        public void ResetClearsInProgressEffectAndFinishedMatch()
        {
            var match = new RallyVersusState();
            match.BeginRally();
            match.RegisterReturn(-1);
            match.CollectItem();
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
            Assert.That(match.SoftenedSide, Is.Zero);
            Assert.That(match.Winner, Is.Zero);
            Assert.That(match.Complete, Is.False);
            Assert.That(match.RallyActive, Is.False);
            Assert.That(match.NextServeSide, Is.EqualTo(-1));
        }
    }
}
