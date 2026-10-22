using System.Numerics;
using Naispo.Kinect;

namespace Naispo.MultiKinect
{
    public enum HandTrackingIssue { None, NoBody, LowConfidence, InvalidPosition, InvalidProjection, OutsideImage }

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
            if (Inspect(source, LeftHandJoint) != HandTrackingIssue.None ||
                Inspect(source, RightHandJoint) != HandTrackingIssue.None) return;
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

        // Readiness and its explanation must share the same checks. A connected camera
        // or a selected body does not yet guarantee usable coordinates for both hands.
        public static HandTrackingIssue Inspect(TrackedPlayerFrame source, int jointIndex)
        {
            if (source == null || !source.Tracked) return HandTrackingIssue.NoBody;
            var joint = source.Joints[jointIndex];
            if (joint.Confidence < 2) return HandTrackingIssue.LowConfidence;
            if ((joint.Flags & 1) == 0 || joint.Z <= 0 ||
                !KinectWire.Finite(joint.X) || !KinectWire.Finite(joint.Y) || !KinectWire.Finite(joint.Z))
                return HandTrackingIssue.InvalidPosition;
            if ((joint.Flags & 2) == 0 || !KinectWire.Finite(joint.U) || !KinectWire.Finite(joint.V))
                return HandTrackingIssue.InvalidProjection;
            if (joint.U < 0 || joint.V < 0 || joint.U >= source.Width || joint.V >= source.Height)
                return HandTrackingIssue.OutsideImage;
            return HandTrackingIssue.None;
        }

        // Empty means both hands passed conversion. Viewport/world-span checks are
        // performed separately by HandWallController and may still reject a wall.
        public static string DescribeReadiness(TrackedPlayerFrame source)
        {
            var left = Inspect(source, LeftHandJoint);
            var right = Inspect(source, RightHandJoint);
            if (left == HandTrackingIssue.NoBody) return "体が見つかりません";
            if (left == HandTrackingIssue.None && right == HandTrackingIssue.None) return "";
            if (left == right) return "両手" + DescribeIssue(left);
            if (left == HandTrackingIssue.None) return "右手" + DescribeIssue(right);
            if (right == HandTrackingIssue.None) return "左手" + DescribeIssue(left);
            return "左手" + DescribeIssue(left) + " / 右手" + DescribeIssue(right);
        }

        static string DescribeIssue(HandTrackingIssue issue)
        {
            switch (issue)
            {
                case HandTrackingIssue.LowConfidence: return "の認識が不安定";
                case HandTrackingIssue.OutsideImage: return "が画角の外";
                default: return "の位置を取得できません";
            }
        }
    }
}
