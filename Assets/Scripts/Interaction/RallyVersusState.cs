namespace Naispo.Interaction
{
    public enum RallyMode { StarCollection, Versus }

    // Sides use -1 for the left player, +1 for the right player, and 0 for none.
    public sealed class RallyVersusState
    {
        public const int PointsToWin = 3;
        public const float SoftReturnMultiplier = .75f;

        public int LeftScore { get; private set; }
        public int RightScore { get; private set; }
        public int LastHitter { get; private set; }
        public int SoftenedSide { get; private set; }
        public int Winner { get; private set; }
        public int NextServeSide { get; private set; }
        public bool Complete => Winner != 0;
        public bool RallyActive { get; private set; }

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

        public float RegisterReturn(int side)
        {
            if (!RallyActive || Complete || !IsPlayerSide(side)) return 1f;
            LastHitter = side;
            if (SoftenedSide != side) return 1f;
            SoftenedSide = 0;
            return SoftReturnMultiplier;
        }

        public bool CollectItem()
        {
            if (!RallyActive || Complete || !IsPlayerSide(LastHitter)) return false;
            SoftenedSide = -LastHitter;
            return true;
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

        void ClearRallyEffects() => LastHitter = SoftenedSide = 0;
        static bool IsPlayerSide(int side) => side == -1 || side == 1;
    }
}
