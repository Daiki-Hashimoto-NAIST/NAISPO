using UnityEngine;

namespace Naispo.Interaction
{
    public sealed partial class CooperativeCoinGame
    {
        RallyLightPhase lightPhase = RallyLightPhase.None;
        float lightPhaseRemaining;
        float lightPendingHidden = RallyVersusState.LightHiddenSeconds;
        bool lightWarningPresented;
        bool spellSuspended;

        public RallyLightPhase LightPhase => lightPhase;
        public float LightPhaseSecondsRemaining => lightPhaseRemaining;
        public bool BallPresentationVisible =>
            !IsVersus || GameplaySuspended || lightPhase != RallyLightPhase.Hidden;

        bool GameplaySuspended => BattleSuspended;

        void BeginLightWarning()
        {
            lightPhase = RallyLightPhase.Warning;
            lightPhaseRemaining = RallyVersusState.LightWarningSeconds;
            lightPendingHidden = RallyVersusState.LightHiddenSeconds;
            lightWarningPresented = false;
        }

        void EndLightPhase()
        {
            lightPhase = RallyLightPhase.None;
            lightPhaseRemaining = 0;
            lightPendingHidden = RallyVersusState.LightHiddenSeconds;
            lightWarningPresented = false;
        }

        void ClearLightPhase()
        {
            if (versus.ActiveSpell == RallySpell.Light) versus.ClearSpell();
            EndLightPhase();
        }

        void CancelLightForContact()
        {
            if (lightPhase == RallyLightPhase.None && versus.ActiveSpell != RallySpell.Light) return;
            ClearLightPhase();
        }

        // Any physical hand-barrier contact cancels Light, even when the contact
        // is rejected for capture or attribution (for example during the .12s
        // same-hand cooldown after a release). Attribution stays with the
        // successful-hit path; this only ends the spell.
        internal void NotifyBarrierContact()
        {
            if (!IsVersus || GameplaySuspended) return;
            CancelLightForContact();
        }

        void CancelLightSpell()
        {
            ClearLightPhase();
        }

        void StepLight(float dt)
        {
            if (lightPhase == RallyLightPhase.None || dt <= 0) return;
            if (lightPhase == RallyLightPhase.Warning)
            {
                lightPhaseRemaining = Mathf.Max(0, lightPhaseRemaining - dt);
                if (lightPhaseRemaining > 0 || !lightWarningPresented) return;
                if (LightProjectionReachesDanger(lightPendingHidden))
                {
                    CancelLightSpell();
                    return;
                }
                lightPhase = RallyLightPhase.Hidden;
                lightPhaseRemaining = lightPendingHidden;
                lightWarningPresented = false;
                // One fixed-position burst marks the transition. It never follows
                // the hidden ball or changes the warning/hidden durations.
                if(spellBallEffects!=null) spellBallEffects.Vanish(ball.transform.position);
                return;
            }

            lightPhaseRemaining -= dt;
            if (lightPhaseRemaining > 0) return;
            EndLightPhase();
            versus.ClearSpell();
        }

        void AdvanceSpellTimers(float dt)
        {
            if (!IsVersus) return;
            versus.StepSpell(dt, ball != null && ball.IsInBarrierImpact);
            StepLight(dt);
        }

        void SyncSpellSuspension()
        {
            bool suspended = GameplaySuspended;
            if (suspended == spellSuspended) return;
            spellSuspended = suspended;
            if (suspended || lightPhase != RallyLightPhase.Hidden) return;
            lightPendingHidden = lightPhaseRemaining;
            lightPhase = RallyLightPhase.Warning;
            lightPhaseRemaining = RallyVersusState.LightWarningSeconds;
            lightWarningPresented = false;
        }

        bool LightProjectionReachesDanger(float horizon)
        {
            if (ball == null || ballCollider == null) return false;
            Vector2 position = ball.Body.position;
            float radius = ballCollider.radius * Mathf.Max(Mathf.Abs(ball.transform.lossyScale.x), Mathf.Abs(ball.transform.lossyScale.y));
            float danger = MatchFloor + 1.2f;
            if (position.y - radius <= danger) return true;
            // Project through the whole horizon even while ascending: near the apex
            // gravity can still drop the ball into the danger band before the hidden
            // phase would end, and Light must cancel before it hides.
            float verticalSpeed = ball.Body.linearVelocity.y;
            float gravity = Physics2D.gravity.y * ball.Body.gravityScale;
            float projected = position.y + verticalSpeed * horizon + .5f * gravity * horizon * horizon;
            return projected - radius <= danger;
        }

        void CheckLightSafetyForRendering()
        {
            // Suspension keeps the ball visible, so no hidden danger exists to cancel.
            if(GameplaySuspended) return;
            if (!IsVersus || lightPhase == RallyLightPhase.None || ball == null || ballCollider == null) return;
            float radius = ballCollider.radius * Mathf.Max(Mathf.Abs(ball.transform.lossyScale.x), Mathf.Abs(ball.transform.lossyScale.y));
            if (ball.IsInBarrierImpact || ball.Body.position.y - radius <= MatchFloor + 1.2f)
                CancelLightSpell();
        }

        void FixedUpdate()
        {
            if(!Application.isPlaying) return;
            if (!configured || !IsVersus || lightPhase == RallyLightPhase.None || ball == null) return;
            if (ball.IsInBarrierImpact) { CancelLightForContact(); return; }
            if (LightProjectionReachesDanger(0)) CancelLightSpell();
        }
    }
}
