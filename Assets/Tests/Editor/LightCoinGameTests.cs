using NUnit.Framework;
using UnityEngine;
using Naispo.Interaction;

namespace Naispo.Tests
{
    public class LightCoinGameTests
    {
        [Test] public void FastBallCollectsCoinBetweenFramesButNotBeyondSegment()
        {
            Assert.IsTrue(LightCoinGame.SweptHit(new Vector2(-2,0),new Vector2(2,0),Vector2.zero,0.3f));
            Assert.IsFalse(LightCoinGame.SweptHit(new Vector2(-2,0),new Vector2(2,0),new Vector2(3,0),0.3f));
            Assert.IsFalse(LightCoinGame.SweptHit(Vector2.zero,Vector2.zero,Vector2.up,0.3f));
        }
        [Test] public void CoinsDisappearOutsideLightWithSoftBoundary()
        {
            Assert.AreEqual(1,LightCoinGame.RevealAmount(0,1.8f));
            Assert.AreEqual(0,LightCoinGame.RevealAmount(1.8f,1.8f));
            Assert.That(LightCoinGame.RevealAmount(1.4f,1.8f),Is.InRange(0.1f,0.9f));
        }
        [Test] public void NarrowBarrierLaunchesHigherAndPreservesAim()
        {
            var narrow=LightCoinGame.BoostReflection(new Vector2(2,1),Vector2.up,0);
            var wide=LightCoinGame.BoostReflection(new Vector2(2,1),Vector2.up,1);
            Assert.That(narrow.y,Is.GreaterThan(wide.y));
            Assert.AreEqual(2,narrow.x,0.001f);
            Assert.That(wide.y,Is.GreaterThan(7));
            Assert.That(LightCoinGame.BoostReflection(new Vector2(20,20),Vector2.up,0).magnitude,Is.LessThanOrEqualTo(13.001f));
            Assert.That(LightCoinGame.BoostReflection(Vector2.zero,Vector2.down,0).y,Is.LessThan(0));
        }
    }
}
