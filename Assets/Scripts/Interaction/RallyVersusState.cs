using UnityEngine;

namespace Naispo.Interaction
{
    public enum RallyMode { StarCollection, Versus }
    public enum RallySpell { None, Fireball, Timer, Light }
    public enum RallyLightPhase { None, Warning, Hidden }

    // Sides use -1 for the left player, +1 for the right player, and 0 for none.
    public sealed class RallyVersusState
    {
        public const int PointsToWin = 3;
        public const float SpellLifetimeSeconds = 6f;
        public const float FireballMultiplier = 1.12f;
        public const float TimerMultiplier = .85f;
        public const float LightWarningSeconds = .45f;
        public const float LightHiddenSeconds = .30f;

        public int LeftScore { get; private set; }
        public int RightScore { get; private set; }
        public int LastHitter { get; private set; }
        public int Winner { get; private set; }
        public int NextServeSide { get; private set; }
        public bool Complete => Winner != 0;
        public bool RallyActive { get; private set; }
        public RallySpell ActiveSpell { get; private set; }
        public int SpellOwner { get; private set; }
        public float SpellSecondsRemaining { get; private set; }

        RallySpell leftPendingSpell;
        RallySpell rightPendingSpell;

        public RallyVersusState() => Reset();

        public void Reset()
        {
            LeftScore = RightScore = Winner = 0;
            NextServeSide = -1;
            RallyActive = false;
            ClearRallyEffects();
        }

        public void BeginRally()
        {
            if (Complete) return;
            RallyActive = true;
            ClearRallyEffects();
        }

        public void RegisterBarrierHit(int side)
        {
            if (!RallyActive || Complete || !IsPlayerSide(side)) return;
            LastHitter = side;
            ClearSpell();
        }

        public bool TryCollectSpell(RallySpell spell)
        {
            if (!RallyActive || Complete || !IsPlayerSide(LastHitter) || !IsCollectibleSpell(spell)) return false;
            if (LastHitter < 0) leftPendingSpell = spell;
            else rightPendingSpell = spell;
            return true;
        }

        // Legacy callers still collect the spell; activation always requires the owner's next return.
        public bool TryActivateSpell(RallySpell spell) => TryCollectSpell(spell);

        public RallySpell PendingSpell(int side)
        {
            if (side == -1) return leftPendingSpell;
            if (side == 1) return rightPendingSpell;
            return RallySpell.None;
        }

        public RallySpell ReleaseSpellForRebound(int playerSide)
        {
            if (!RallyActive || Complete || !IsPlayerSide(playerSide) || playerSide != LastHitter)
                return RallySpell.None;
            RallySpell spell = PendingSpell(playerSide);
            if (spell == RallySpell.None) return RallySpell.None;
            if (playerSide < 0) leftPendingSpell = RallySpell.None;
            else rightPendingSpell = RallySpell.None;
            ActiveSpell = spell;
            SpellOwner = playerSide;
            SpellSecondsRemaining = spell == RallySpell.Light ? 0 : SpellLifetimeSeconds;
            return spell;
        }

        public void StepSpell(float dt, bool compression)
        {
            if (dt <= 0 || compression || Complete) return;
            if (ActiveSpell != RallySpell.Fireball && ActiveSpell != RallySpell.Timer) return;
            SpellSecondsRemaining -= dt;
            if (SpellSecondsRemaining <= 0) ClearSpell();
        }

        public static float SpellMultiplier(RallySpell spell)
        {
            if (spell == RallySpell.Fireball) return FireballMultiplier;
            if (spell == RallySpell.Timer) return TimerMultiplier;
            return 1f;
        }

        public void ClearSpell()
        {
            ActiveSpell = RallySpell.None;
            SpellOwner = 0;
            SpellSecondsRemaining = 0;
        }

        public bool AwardPoint(int losingSide)
        {
            if (!RallyActive || Complete || !IsPlayerSide(losingSide)) return false;
            int scoringSide = -losingSide;
            if (scoringSide < 0) LeftScore++;
            else RightScore++;
            if (LeftScore >= PointsToWin || RightScore >= PointsToWin) Winner = scoringSide;
            NextServeSide = losingSide;
            RallyActive = false;
            ClearRallyEffects();
            return true;
        }

        void ClearRallyEffects()
        {
            LastHitter = 0;
            leftPendingSpell = rightPendingSpell = RallySpell.None;
            ClearSpell();
        }
        static bool IsPlayerSide(int side) => side == -1 || side == 1;
        static bool IsCollectibleSpell(RallySpell spell) =>
            spell == RallySpell.Fireball || spell == RallySpell.Timer || spell == RallySpell.Light;
    }
}
