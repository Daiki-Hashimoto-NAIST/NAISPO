using UnityEngine;

namespace Naispo.Interaction
{
    public sealed partial class CooperativeCoinGame
    {
        RallySpellBallEffects spellBallEffects;
        public RallySpellBallEffects SpellBallEffects => spellBallEffects;

        void BuildSpellBallPresentation()
        {
            spellBallEffects = CreateChild("Spell ball effects", arenaRoot.transform).AddComponent<RallySpellBallEffects>();
            spellBallEffects.Initialize(ballCoreSprite);
        }

        void SyncSpellBallPresentation()
        {
            if (spellBallEffects == null || ball == null) return;
            bool liveCourt = IsVersus && !versus.Complete && serveRemaining < 0;
            spellBallEffects.SetState(liveCourt ? ActiveSpell : RallySpell.None,
                ball.transform.position, ball.Body.linearVelocity, liveCourt,
                GameplaySuspended, !BallPresentationVisible);
        }

        void AdvanceSpellBallPresentation(float dt)
        {
            if (spellBallEffects == null || ball == null) return;
            // RefreshLighting has already resolved contact/floor safety and hidden
            // visibility. A hidden sphere must never leave a moving trail.
            spellBallEffects.Step(dt, ball.transform.position, ball.Body.linearVelocity);
        }

        Color SpellBallTint(Color ivory)
        {
            if (!IsVersus || ActiveSpell == RallySpell.None) return ivory;
            return Color.Lerp(ivory, SpellColor(ActiveSpell), .68f);
        }
    }
}
