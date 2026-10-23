using UnityEngine;

namespace Naispo.Interaction
{
    // Velocity uses camera receipt intervals, never repeated physics ticks.
    // Pose continuity and motion confidence deliberately have separate lifetimes.
    public sealed class HandMotionEstimator
    {
        public const float MaximumSampleAge = .15f;
        const float MinimumStableSeconds = .12f;
        const float SmoothingSeconds = .07f;
        const float MaximumEndpointStep = .7f;
        const int MinimumSamples = 3;
        bool hasSample, ready;
        uint owner;
        long lastRevision = long.MinValue;
        double firstSampleAt, lastSampleAt;
        int samples;
        Vector2 previousLeft, previousRight, leftVelocity, rightVelocity;

        public void Reset()
        {
            ClearHistory();
            lastRevision = long.MinValue;
        }

        void ClearHistory()
        {
            hasSample = ready = false;
            samples = 0;
            firstSampleAt = lastSampleAt = double.NegativeInfinity;
            previousLeft = previousRight = leftVelocity = rightVelocity = Vector2.zero;
        }

        public void Observe(long revision, uint identity, bool valid, Vector2 left, Vector2 right,
            double receivedAt, double now)
        {
            if (!valid || !Finite(left) || !Finite(right) || !Finite(receivedAt) || !Finite(now) ||
                now < receivedAt || now - receivedAt > MaximumSampleAge)
            {
                ClearHistory();
                lastRevision = revision;
                return;
            }
            if (hasSample && (identity != owner || revision < lastRevision)) ClearHistory();
            if (revision == lastRevision) return;
            lastRevision = revision;
            double interval = receivedAt - lastSampleAt;
            Vector2 leftStep = left - previousLeft, rightStep = right - previousRight;
            if (!hasSample || interval <= .000001 || interval > MaximumSampleAge ||
                leftStep.sqrMagnitude > MaximumEndpointStep * MaximumEndpointStep ||
                rightStep.sqrMagnitude > MaximumEndpointStep * MaximumEndpointStep)
            {
                ClearHistory();
                hasSample = true;
                owner = identity;
                firstSampleAt = lastSampleAt = receivedAt;
                previousLeft = left;
                previousRight = right;
                samples = 1;
                return;
            }
            float dt = (float)interval;
            float smoothing = 1 - Mathf.Exp(-dt / SmoothingSeconds);
            leftVelocity = Vector2.Lerp(leftVelocity, leftStep / dt, smoothing);
            rightVelocity = Vector2.Lerp(rightVelocity, rightStep / dt, smoothing);
            previousLeft = left;
            previousRight = right;
            lastSampleAt = receivedAt;
            samples++;
            ready = samples >= MinimumSamples && receivedAt - firstSampleAt >= MinimumStableSeconds - .000001;
        }

        public bool TryGetVelocity(double now, out Vector2 left, out Vector2 right, out double sampledAt)
        {
            left = right = Vector2.zero;
            sampledAt = lastSampleAt;
            if (!Finite(now) || now < lastSampleAt || now - lastSampleAt > MaximumSampleAge)
            {
                ClearHistory();
                return false;
            }
            if (!ready) return false;
            left = leftVelocity;
            right = rightVelocity;
            return true;
        }

        static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
