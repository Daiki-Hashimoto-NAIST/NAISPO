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

        [Test] public void FlatBarrierPassesInwardAndTiltCanChangeAimWithoutExcessSpeed()
        {
            Vector2 left=CooperativeCoinGame.CooperativeReflection(Vector2.zero,Vector2.up,.5f,-4.4f,8.4f);
            Vector2 right=CooperativeCoinGame.CooperativeReflection(Vector2.zero,Vector2.up,.5f,4.4f,8.4f);
            Assert.That(left.x,Is.GreaterThan(2)); Assert.That(right.x,Is.LessThan(-2));
            Assert.That(left.y,Is.GreaterThan(6.5f));
            Vector2 aimed=CooperativeCoinGame.CooperativeReflection(Vector2.zero,new Vector2(.5f,1),.5f,-4.4f,8.4f);
            Assert.That(aimed.x,Is.GreaterThan(left.x));
            Assert.That(aimed.magnitude,Is.LessThan(9.5f));
            Vector2 narrow=CooperativeCoinGame.CooperativeReflection(Vector2.zero,Vector2.up,0,-4.4f,8.4f);
            Vector2 wide=CooperativeCoinGame.CooperativeReflection(Vector2.zero,Vector2.up,1,-4.4f,8.4f);
            Assert.That(narrow.y,Is.GreaterThan(wide.y));
            Assert.That(CooperativeCoinGame.CooperativeReflection(Vector2.zero,Vector2.down,0,0,8.4f).y,Is.LessThan(0));
            float height=left.y*left.y/(2*9.81f*.55f)-2.25f;
            Assert.That(height,Is.InRange(2.3f,3.5f),"A default pass should reach the central arc without launching into the HUD.");
            float demoTilt=5*Mathf.Deg2Rad;
            Vector2 demo=CooperativeCoinGame.CooperativeReflection(Vector2.zero,
                new Vector2(Mathf.Sin(demoTilt),Mathf.Cos(demoTilt)),.5833333f,-4.44f,8.4f);
            float centerTime=4.44f/demo.x;
            float centerY=-2.35f+demo.y*centerTime-.5f*9.81f*.55f*centerTime*centerTime;
            Assert.That(Mathf.Abs(centerY-2.4f),Is.LessThan(.49f),"Initial demo hand width and tilt must reach the middle coin.");
        }
    }
}
