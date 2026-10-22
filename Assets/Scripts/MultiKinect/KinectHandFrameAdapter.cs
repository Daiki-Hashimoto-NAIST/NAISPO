using System.Numerics;
using Naispo.Kinect;

namespace Naispo.MultiKinect
{
    // Preserve the original barrier's calibrated pixel positions and physical hand span.
    // Kinect Body Tracking joint IDs: HAND_LEFT = 8, HAND_RIGHT = 15.
    public static class KinectHandFrameAdapter
    {
        public const int LeftHandJoint = 8, RightHandJoint = 15;

        public static void CopyTo(TrackedPlayerFrame source, BodyFrameData destination, uint gameBodyId)
        {
            destination.EnsureSize(source.Width, source.Height);
            destination.Sequence = source.Sequence;
            destination.TimestampSeconds = source.TrackingTimestamp;
            destination.BodyCount = source.Tracked ? 1 : 0;
            destination.BodyIds[0] = gameBodyId;
            destination.HasColor = false;
            destination.Hands[0] = default;
            for (int i = 0; i < source.Mask.Length; i++)
                destination.BodyIndexMap[i] = (byte)(source.Tracked && source.Mask[i] == 1 ? 0 : 255);
            if (!source.Tracked) return;

            var left = source.Joints[LeftHandJoint];
            var right = source.Joints[RightHandJoint];
            if (!ValidHand(left, source.Width, source.Height) || !ValidHand(right, source.Width, source.Height)) return;
            float separation = Vector3.Distance(new Vector3(left.X, left.Y, left.Z), new Vector3(right.X, right.Y, right.Z)) * .001f;
            if (!KinectWire.Finite(separation)) return;
            destination.Hands[0] = new BodyFrameData.HandPair
            {
                Left = new Vector2(left.U, left.V),
                Right = new Vector2(right.U, right.V),
                SeparationMeters = separation,
                Valid = true
            };
        }

        static bool ValidHand(WireJoint joint, int width, int height) =>
            joint.Confidence >= 2 && (joint.Flags & 3) == 3 && joint.Z > 0 &&
            KinectWire.Finite(joint.X) && KinectWire.Finite(joint.Y) && KinectWire.Finite(joint.Z) &&
            KinectWire.Finite(joint.U) && KinectWire.Finite(joint.V) &&
            joint.U >= 0 && joint.V >= 0 && joint.U < width && joint.V < height;
    }
}
