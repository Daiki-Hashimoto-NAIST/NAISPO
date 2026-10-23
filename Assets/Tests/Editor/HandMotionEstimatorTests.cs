using NUnit.Framework;
using UnityEngine;
using Naispo.Interaction;

namespace Naispo.Tests
{
    public sealed class HandMotionEstimatorTests
    {
        static readonly Vector2 Left = new Vector2(-1, -2), Right = new Vector2(1, -2);

        static void Sample(HandMotionEstimator motion, long revision, double time, float y,
            bool valid = true, uint owner = 7) => motion.Observe(revision, owner, valid,
                Left + Vector2.up * y, Right + Vector2.up * y, time, time);

        static HandMotionEstimator Moving()
        {
            var motion = new HandMotionEstimator();
            for (int i = 0; i < 4; i++) Sample(motion, i + 1, i * .04, i * .04f);
            Assert.That(motion.TryGetVelocity(.12, out _, out _, out _), Is.True);
            return motion;
        }

        [Test]
        public void NewSamplesMustCoverStableTimeBeforeSmoothedVelocityIsAvailable()
        {
            var motion = new HandMotionEstimator();
            for (int i = 0; i < 3; i++)
            {
                Sample(motion, i + 1, i * .04, i * .04f);
                Assert.That(motion.TryGetVelocity(i * .04, out var left, out var right, out _), Is.False);
                Assert.That(left, Is.EqualTo(Vector2.zero));
                Assert.That(right, Is.EqualTo(Vector2.zero));
            }
            Sample(motion, 4, .12, .12f);
            Assert.That(motion.TryGetVelocity(.12, out var leftVelocity, out var rightVelocity, out var sampledAt), Is.True);
            Assert.That(leftVelocity.x, Is.Zero);
            Assert.That(leftVelocity.y, Is.InRange(.7f, 1f));
            Assert.That(rightVelocity, Is.EqualTo(leftVelocity));
            Assert.That(sampledAt, Is.EqualTo(.12));
        }

        [Test]
        public void OneLongIntervalCannotReplaceMultipleSamples()
        {
            var motion = new HandMotionEstimator();
            Sample(motion, 1, 0, 0);
            Sample(motion, 2, .13, .13f);
            Assert.That(motion.TryGetVelocity(.13, out _, out _, out _), Is.False);
            Sample(motion, 3, .14, .14f);
            Assert.That(motion.TryGetVelocity(.14, out _, out _, out _), Is.True);
        }

        [Test]
        public void CachedFrameCannotAlterVelocityOrExtendItsExpiry()
        {
            var motion = Moving();
            motion.TryGetVelocity(.12, out var original, out _, out _);
            Sample(motion, 4, .24, 10);
            Assert.That(motion.TryGetVelocity(.24, out var velocity, out _, out var sampledAt), Is.True);
            Assert.That(velocity, Is.EqualTo(original));
            Assert.That(sampledAt, Is.EqualTo(.12));
            Assert.That(motion.TryGetVelocity(.271, out velocity, out _, out _), Is.False);
            Assert.That(velocity, Is.EqualTo(Vector2.zero));
            Sample(motion, 4, .28, 10);
            Assert.That(motion.TryGetVelocity(.28, out _, out _, out _), Is.False);
        }

        [Test]
        public void MissingHandsClearMotionAndQuickRecoveryStillNeedsFreshHistory()
        {
            var motion = Moving();
            Sample(motion, 5, .14, 20, false);
            Assert.That(motion.TryGetVelocity(.14, out _, out _, out _), Is.False);
            Sample(motion, 6, .16, .16f);
            Sample(motion, 7, .20, .20f);
            Sample(motion, 8, .24, .24f);
            Assert.That(motion.TryGetVelocity(.24, out _, out _, out _), Is.False);
            Sample(motion, 9, .28, .28f);
            Assert.That(motion.TryGetVelocity(.28, out var velocity, out _, out _), Is.True);
            Assert.That(velocity.y, Is.InRange(.7f, 1f));
        }

        [Test]
        public void ChangedOwnerOrRestartedSequenceCannotBorrowVelocity()
        {
            var motion = Moving();
            Sample(motion, 5, .16, .16f, owner: 8);
            Assert.That(motion.TryGetVelocity(.16, out _, out _, out _), Is.False);
            motion = Moving();
            Sample(motion, 1, .16, .16f);
            Assert.That(motion.TryGetVelocity(.16, out _, out _, out _), Is.False);
        }

        [Test]
        public void JumpInEitherEndpointClearsBothVelocities()
        {
            var motion = Moving();
            motion.Observe(5, 7, true, Left + Vector2.up * .16f, Right + Vector2.up * 1.2f, .16, .16);
            Assert.That(motion.TryGetVelocity(.16, out var left, out var right, out _), Is.False);
            Assert.That(left, Is.EqualTo(Vector2.zero));
            Assert.That(right, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void LongGapStaleReceiptAndInvalidClockAllResetMotion()
        {
            var motion = Moving();
            Sample(motion, 5, .29, .29f);
            Assert.That(motion.TryGetVelocity(.29, out _, out _, out _), Is.False);
            motion = Moving();
            motion.Observe(5, 7, true, Left, Right, .13, .30);
            Assert.That(motion.TryGetVelocity(.30, out _, out _, out _), Is.False);
            motion = Moving();
            motion.Observe(5, 7, true, Left, Right, .16, .15);
            Assert.That(motion.TryGetVelocity(.16, out _, out _, out _), Is.False);
            motion = Moving();
            motion.Observe(5, 7, true, Left, Right, double.NaN, .16);
            Assert.That(motion.TryGetVelocity(.16, out _, out _, out _), Is.False);
        }

        [Test]
        public void NonFinitePositionsAndZeroSampleIntervalsCannotProduceASwing()
        {
            var motion = Moving();
            motion.Observe(5, 7, true, new Vector2(float.NaN, 0), Right, .16, .16);
            Assert.That(motion.TryGetVelocity(.16, out _, out _, out _), Is.False);
            motion = Moving();
            Sample(motion, 5, .12, .13f);
            Assert.That(motion.TryGetVelocity(.12, out _, out _, out _), Is.False);
        }

        [Test]
        public void EndpointsRetainIndependentDirectionsAndMotionSettlesWhenHandsStop()
        {
            var motion = new HandMotionEstimator();
            for (int i = 0; i < 4; i++) motion.Observe(i + 1, 7, true,
                Left + Vector2.up * (i * .04f), Right + Vector2.down * (i * .02f), i * .04, i * .04);
            Assert.That(motion.TryGetVelocity(.12, out var left, out var right, out _), Is.True);
            Assert.That(left.y, Is.GreaterThan(.7f));
            Assert.That(right.y, Is.LessThan(-.35f));
            for (int i = 4; i < 12; i++) motion.Observe(i + 1, 7, true,
                Left + Vector2.up * .12f, Right + Vector2.down * .06f, i * .04, i * .04);
            motion.TryGetVelocity(.44, out left, out right, out _);
            Assert.That(left.magnitude, Is.LessThan(.01f));
            Assert.That(right.magnitude, Is.LessThan(.01f));
        }

        [Test]
        public void ExplicitResetRequiresNewHistory()
        {
            var motion = Moving();
            motion.Reset();
            Assert.That(motion.TryGetVelocity(.12, out _, out _, out _), Is.False);
            Sample(motion, 5, .16, .16f);
            Assert.That(motion.TryGetVelocity(.16, out _, out _, out _), Is.False);
        }
    }
}
