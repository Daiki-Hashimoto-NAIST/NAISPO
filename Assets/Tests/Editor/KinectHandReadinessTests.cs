using NUnit.Framework;
using Naispo.Kinect;
using Naispo.MultiKinect;

namespace Naispo.Tests
{
    public sealed class KinectHandReadinessTests
    {
        static TrackedPlayerFrame Frame()
        {
            var frame = new TrackedPlayerFrame { Tracked = true };
            frame.Resize(320, 288);
            frame.Joints[KinectHandFrameAdapter.LeftHandJoint] = new WireJoint
                { X = -300, Y = 50, Z = 2000, U = 100, V = 140, Confidence = 2, Flags = 3 };
            frame.Joints[KinectHandFrameAdapter.RightHandJoint] = new WireJoint
                { X = 300, Y = 50, Z = 2000, U = 220, V = 140, Confidence = 2, Flags = 3 };
            return frame;
        }

        [Test]
        public void ValidHandsHaveNoWarningAndRemainUsable()
        {
            var frame = Frame();
            // A body can be selected with lower pelvis confidence. Both hands still
            // require Medium confidence, as in the original single-camera game.
            frame.Joints[0] = new WireJoint { Confidence = 1 };
            var destination = new BodyFrameData();
            KinectHandFrameAdapter.CopyTo(frame, destination, 1);
            Assert.IsTrue(destination.Hands[0].Valid);
            Assert.AreEqual("", KinectHandFrameAdapter.DescribeReadiness(frame));
        }

        [TestCase(0, HandTrackingIssue.LowConfidence)]
        [TestCase(1, HandTrackingIssue.InvalidPosition)]
        [TestCase(2, HandTrackingIssue.InvalidProjection)]
        [TestCase(3, HandTrackingIssue.OutsideImage)]
        public void EachRejectionExplainsTheUnusableHand(int scenario, HandTrackingIssue expected)
        {
            var frame = Frame();
            var hand = frame.Joints[KinectHandFrameAdapter.LeftHandJoint];
            switch (scenario)
            {
                case 0: hand.Confidence = 1; break;
                case 1: hand.Z = 0; break;
                case 2: hand.Flags = 1; break;
                case 3: hand.U = frame.Width; break;
            }
            frame.Joints[KinectHandFrameAdapter.LeftHandJoint] = hand;
            Assert.AreEqual(expected, KinectHandFrameAdapter.Inspect(frame, KinectHandFrameAdapter.LeftHandJoint));
            Assert.AreEqual(HandTrackingIssue.None, KinectHandFrameAdapter.Inspect(frame, KinectHandFrameAdapter.RightHandJoint));
            StringAssert.StartsWith("左手", KinectHandFrameAdapter.DescribeReadiness(frame));
            var destination = new BodyFrameData();
            KinectHandFrameAdapter.CopyTo(frame, destination, 1);
            Assert.IsFalse(destination.Hands[0].Valid);
        }

        [Test]
        public void RightHandAndBothHandsAreReportedSeparately()
        {
            var frame = Frame();
            frame.Joints[KinectHandFrameAdapter.RightHandJoint].Confidence = 1;
            Assert.AreEqual("右手の認識が不安定", KinectHandFrameAdapter.DescribeReadiness(frame));
            frame.Joints[KinectHandFrameAdapter.LeftHandJoint].Confidence = 1;
            Assert.AreEqual("両手の認識が不安定", KinectHandFrameAdapter.DescribeReadiness(frame));
        }

        [Test]
        public void MixedHandProblemsPreserveBothExplanations()
        {
            var frame = Frame();
            frame.Joints[KinectHandFrameAdapter.LeftHandJoint].U = -1;
            frame.Joints[KinectHandFrameAdapter.RightHandJoint].Confidence = 1;
            Assert.AreEqual("左手が画角の外 / 右手の認識が不安定", KinectHandFrameAdapter.DescribeReadiness(frame));
        }

        [Test]
        public void MissingBodyDoesNotMisreportHandConfidence()
        {
            var frame = Frame();
            frame.Tracked = false;
            Assert.AreEqual(HandTrackingIssue.NoBody, KinectHandFrameAdapter.Inspect(frame, KinectHandFrameAdapter.LeftHandJoint));
            Assert.AreEqual("体が見つかりません", KinectHandFrameAdapter.DescribeReadiness(frame));
            Assert.AreEqual("体が見つかりません", KinectHandFrameAdapter.DescribeReadiness(null));
        }
    }
}
