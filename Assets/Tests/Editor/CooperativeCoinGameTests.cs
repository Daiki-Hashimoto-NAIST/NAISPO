using NUnit.Framework;
using UnityEngine;
using Naispo.Interaction;

namespace Naispo.Tests
{
    public sealed class CooperativeCoinGameTests
    {
        [Test] public void EitherPlayerContributesToOneScoreAndRepeatedContactCountsOnce()
        {
            var round=new CooperativeCoinRound(3);
            Assert.IsTrue(round.Collect(0));
            Assert.IsTrue(round.Collect(2));
            Assert.IsFalse(round.Collect(0));
            Assert.That(round.TeamScore,Is.EqualTo(2));
            Assert.That(round.CollectedCount,Is.EqualTo(2));
            Assert.IsFalse(round.AdvanceRound(),"A partly collected arc must remain available.");
        }

        [Test] public void MissRetainsCollectedCoinsAndFollowingServesAlternate()
        {
            var round=new CooperativeCoinRound(9);
            Assert.That(round.TakeServeSide(),Is.EqualTo(-1));
            round.Collect(0); round.Collect(4);
            round.RegisterMiss();
            Assert.That(round.TakeServeSide(),Is.EqualTo(1));
            round.RegisterMiss();
            Assert.That(round.TakeServeSide(),Is.EqualTo(-1));
            Assert.IsTrue(round.IsCollected(0)); Assert.IsTrue(round.IsCollected(4));
            Assert.That(round.TeamScore,Is.EqualTo(2));
            Assert.That(round.Misses,Is.EqualTo(2));
        }

        [Test] public void CompletedRoundKeepsTeamTotalAndRestartClearsEverything()
        {
            var round=new CooperativeCoinRound(3);
            for(int i=0;i<3;i++) round.Collect(i);
            Assert.IsTrue(round.Complete);
            Assert.IsTrue(round.AdvanceRound());
            Assert.That(round.Round,Is.EqualTo(2));
            Assert.That(round.CollectedCount,Is.Zero);
            Assert.That(round.TeamScore,Is.EqualTo(3));
            Assert.IsTrue(round.Collect(0));
            Assert.That(round.TeamScore,Is.EqualTo(4));
            round.TakeServeSide(); round.RegisterMiss(); round.Reset();
            Assert.That(round.Round,Is.EqualTo(1));
            Assert.That(round.TeamScore,Is.Zero); Assert.That(round.Misses,Is.Zero);
            Assert.That(round.TakeServeSide(),Is.EqualTo(-1));
        }

        [Test] public void FastBallCollectsAlongTravelButDoesNotExtendPastEitherEndpoint()
        {
            Assert.IsTrue(CooperativeCoinGame.SweptHit(new Vector2(-3,1),new Vector2(3,1),new Vector2(0,1.2f),.3f));
            Assert.IsFalse(CooperativeCoinGame.SweptHit(new Vector2(-3,1),new Vector2(3,1),new Vector2(4,1),.3f));
            Assert.IsFalse(CooperativeCoinGame.SweptHit(new Vector2(-3,1),new Vector2(3,1),new Vector2(-4,1),.3f));
            Assert.IsFalse(CooperativeCoinGame.SweptHit(Vector2.zero,Vector2.zero,Vector2.up,.3f));
        }

        [Test] public void PatternsStayAboveNetAndInsidePlayableArea()
        {
            for(int round=1;round<=6;round++)
            {
                Vector2 left=CooperativeCoinGame.ParabolicCoinPosition(0,round,Vector2.zero,8.5f,5);
                Vector2 right=CooperativeCoinGame.ParabolicCoinPosition(1,round,Vector2.zero,8.5f,5);
                Vector2 apex=CooperativeCoinGame.ParabolicCoinPosition(.5f,round,Vector2.zero,8.5f,5);
                Assert.That(apex.x,Is.EqualTo(0).Within(.001));
                Assert.That(left.x,Is.EqualTo(-right.x).Within(.001));
                Assert.That(apex.y,Is.InRange(2,2.7f));
                for(int i=0;i<9;i++)
                {
                    Vector2 p=CooperativeCoinGame.ParabolicCoinPosition(i/8f,round,Vector2.zero,8.5f,5);
                    Assert.That(p.x,Is.InRange(-5,5));
                    Assert.That(p.y,Is.InRange(-.31f,2.7f));
                    Assert.That(p.y,Is.GreaterThan(-4.4f+3.1f+.4f),"Coin must remain clear even of the highest adjustable net.");
                }
            }
        }

        [TestCase(6.5f)]
        [TestCase(8.4f)]
        [TestCase(10.5f)]
        public void IncreasingHandWidthSoftensTheReturnContinuouslyOnEitherSide(float speed)
        {
            float previous=float.PositiveInfinity;
            for(int i=0;i<=20;i++)
            {
                float width=i/20f;
                Vector2 left=CooperativeCoinGame.CooperativeReflection(Vector2.zero,Vector2.up,width,-4.4f,speed);
                Vector2 right=CooperativeCoinGame.CooperativeReflection(Vector2.zero,Vector2.up,width,4.4f,speed);
                Assert.That(left.magnitude,Is.LessThan(previous));
                Assert.That(left.x,Is.EqualTo(0).Within(.001f)); Assert.That(left.y,Is.GreaterThan(0));
                Assert.That(right.x,Is.EqualTo(left.x).Within(.001f));
                Assert.That(right.y,Is.EqualTo(left.y).Within(.001f));
                previous=left.magnitude;
            }
            Vector2 narrow=CooperativeCoinGame.CooperativeReflection(Vector2.zero,Vector2.up,0,-4.4f,speed);
            Assert.That(narrow.magnitude,Is.GreaterThan(previous*1.9f));
        }

        [Test] public void UndersideAndSideContactsAlsoRespectHandWidthWithoutTurningUpward()
        {
            foreach(Vector2 normal in new[] {Vector2.down,Vector2.left})
            {
                Vector2 narrow=CooperativeCoinGame.CooperativeReflection(-normal*8,normal,0,0,8.4f);
                Vector2 wide=CooperativeCoinGame.CooperativeReflection(-normal*8,normal,1,0,8.4f);
                Assert.That(Vector2.Dot(wide,normal),Is.GreaterThan(0));
                Assert.That(narrow.magnitude,Is.GreaterThan(wide.magnitude*1.9f));
                Assert.That(narrow.y,Is.LessThanOrEqualTo(0));
                Assert.That(wide.y,Is.LessThanOrEqualTo(0));
            }
        }

        [Test] public void FlatBarrierReflectsVerticallyOnBothCourtsWithoutAnAutomaticPass()
        {
            foreach(float court in new[] {-4.4f,4.4f})
            {
                Vector2 returned=CooperativeCoinGame.CooperativeReflection(Vector2.down*8,Vector2.up,.5f,court,8.4f);
                Assert.That(returned.x,Is.EqualTo(0).Within(.0001f));
                Assert.That(returned.y,Is.GreaterThan(0));
            }
        }

        [TestCase(-3f)]
        [TestCase(3f)]
        public void FlatBarrierKeepsTheIncomingHorizontalDirectionAndReflectionAngle(float horizontal)
        {
            Vector2 returned=CooperativeCoinGame.CooperativeReflection(new Vector2(horizontal,-6),Vector2.up,.5f,-4.4f,8.4f);
            Vector2 expected=new Vector2(horizontal,6).normalized;
            Assert.That(returned.normalized.x,Is.EqualTo(expected.x).Within(.0001f));
            Assert.That(returned.normalized.y,Is.EqualTo(expected.y).Within(.0001f));
        }

        [Test] public void TiltedBarrierUsesItsNormalRatherThanAimingAtTheOtherCourt()
        {
            Vector2 returned=CooperativeCoinGame.CooperativeReflection(Vector2.down*8,new Vector2(.5f,1),.5f,4.4f,8.4f);
            Assert.That(returned.normalized.x,Is.EqualTo(.8f).Within(.0001f));
            Assert.That(returned.normalized.y,Is.EqualTo(.6f).Within(.0001f));
            Vector2 alreadyLeaving=CooperativeCoinGame.CooperativeReflection(new Vector2(3,6),Vector2.up,.5f,-4.4f,8.4f);
            Assert.That(Vector2.Angle(alreadyLeaving,new Vector2(3,6)),Is.LessThan(.01f),"A separating velocity must not be reflected back into the barrier.");
        }

        [Test] public void IncomingSpeedIsPreservedBetweenTheLowSpeedAssistAndSafetyLimit()
        {
            Vector2 slow=CooperativeCoinGame.CooperativeReflection(Vector2.down*7.3f,Vector2.up,.4f,0,8.4f);
            Vector2 fast=CooperativeCoinGame.CooperativeReflection(Vector2.down*8.1f,Vector2.up,.4f,0,8.4f);
            Assert.That(fast.magnitude/slow.magnitude,Is.EqualTo(8.1f/7.3f).Within(.0001f));
            Vector2 stopped=CooperativeCoinGame.CooperativeReflection(Vector2.zero,Vector2.up,.4f,0,8.4f);
            Vector2 crawling=CooperativeCoinGame.CooperativeReflection(Vector2.down*.2f,Vector2.up,.4f,0,8.4f);
            Assert.That(stopped.y,Is.GreaterThan(4),"A nearly stationary ball must still leave a stationary barrier.");
            Assert.That(crawling.magnitude,Is.EqualTo(stopped.magnitude).Within(.0001f));
            Assert.That(slow.magnitude,Is.GreaterThan(stopped.magnitude));
        }

        [TestCase(6.5f)]
        [TestCase(10.5f)]
        public void HandMotionAddsAtMostTwentyPercentWithoutChangingTheReflectionDirection(float speed)
        {
            Vector2 incoming=new Vector2(3,-8);
            Vector2 ordinary=CooperativeCoinGame.CooperativeReflection(incoming,Vector2.up,.5f,0,speed);
            Vector2 half=CooperativeCoinGame.CooperativeReflection(incoming,Vector2.up,.5f,0,speed,.5f);
            Vector2 full=CooperativeCoinGame.CooperativeReflection(incoming,Vector2.up,.5f,0,speed,1);
            Vector2 extreme=CooperativeCoinGame.CooperativeReflection(incoming*100,Vector2.up,0,0,speed,100);
            Assert.That(half.magnitude,Is.EqualTo(ordinary.magnitude*1.1f).Within(.0001f));
            Assert.That(full.magnitude,Is.EqualTo(ordinary.magnitude*1.2f).Within(.0001f));
            Assert.That(Vector2.Angle(full,ordinary),Is.LessThan(.01f));
            Assert.That(extreme.magnitude,Is.EqualTo(speed*1.14f*1.2f).Within(.0001f));
            Assert.That(CooperativeCoinGame.CooperativeReflection(incoming,Vector2.up,.5f,0,speed,-1).magnitude,
                Is.EqualTo(ordinary.magnitude).Within(.0001f));
        }
    }
}
