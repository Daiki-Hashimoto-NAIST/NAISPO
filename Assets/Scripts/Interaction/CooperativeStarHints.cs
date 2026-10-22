using UnityEngine;

namespace Naispo.Interaction
{
    public sealed partial class CooperativeCoinGame
    {
        const float StarHintDelay = 8f;
        const float StarHintInterval = 4f;
        const float StarHintDuration = .7f;
        SpriteRenderer starHintSpeck, starHintHalo;
        float starHintWait = StarHintDelay, starHintAge = -1;
        int starHintIndex = -1, starHintCursor, starHintSequence;
        Vector2 starHintOrigin;

        void BuildStarHintArtwork()
        {
            starHintSpeck = CreateChild("Star hint speck", arenaRoot.transform).AddComponent<SpriteRenderer>();
            starHintHalo = CreateChild("Star hint halo", arenaRoot.transform).AddComponent<SpriteRenderer>();
            // Reuse the round light texture: the hint never exposes a star silhouette.
            starHintSpeck.sprite = starHintHalo.sprite = haloSprite;
            starHintSpeck.sortingOrder = 7;
            starHintHalo.sortingOrder = 6;
            starHintSpeck.transform.localScale = Vector3.one * .12f;
            starHintHalo.transform.localScale = Vector3.one * .38f;
            ResetStarHint();
        }

        void ResetStarHint()
        {
            starHintWait = StarHintDelay;
            starHintAge = -1;
            starHintIndex = -1;
            starHintCursor = starHintSequence = 0;
            if(starHintSpeck != null) starHintSpeck.enabled = starHintHalo.enabled = false;
        }

        // Called only while the rally is advancing, after the current frame's pickups.
        void AdvanceStarHint(float dt)
        {
            int remaining = CoinsPerRound - progress.CollectedCount;
            if(IsVersus || !ballLightEnabled || remaining < 1 || remaining > 2)
            {
                ResetStarHint();
                return;
            }
            if(starHintIndex >= 0)
            {
                starHintAge += dt;
                if(starHintAge >= StarHintDuration || progress.IsCollected(starHintIndex) ||
                    CoinVisibility(starHintIndex) > 0)
                {
                    starHintIndex = -1;
                    starHintAge = -1;
                    starHintWait = StarHintInterval;
                }
                return;
            }
            starHintWait -= dt;
            if(starHintWait > 0) return;
            starHintWait = StarHintInterval;
            // Take turns when two stars remain, skipping any already lit by the ball.
            for(int offset = 0; offset < CoinsPerRound; offset++)
            {
                int index = (starHintCursor + offset) % CoinsPerRound;
                if(progress.IsCollected(index) || CoinVisibility(index) > 0) continue;
                starHintIndex = index;
                starHintCursor = (index + 1) % CoinsPerRound;
                starHintAge = 0;
                float angle = index * 2.399963f + Round * .91f + starHintSequence++ * 1.37f;
                // Offset each flash from the star, giving an area to explore rather than a marker.
                starHintOrigin = coinPositions[index] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .42f;
                break;
            }
        }

        void RefreshStarHint()
        {
            if(starHintSpeck == null) return;
            bool show = CanAdvanceBall && !IsVersus && ballLightEnabled && starHintIndex >= 0 &&
                !progress.IsCollected(starHintIndex) && CoinVisibility(starHintIndex) <= 0;
            starHintSpeck.enabled = starHintHalo.enabled = show;
            if(!show) return;
            float t = Mathf.Clamp01(starHintAge / StarHintDuration);
            float alpha = Mathf.Pow(Mathf.Sin(Mathf.PI * t), 2);
            Vector2 position = starHintOrigin + Vector2.up * (.08f * t);
            starHintSpeck.transform.position = starHintHalo.transform.position = position;
            starHintSpeck.color = new Color(1, .94f, .68f, alpha * .9f);
            starHintHalo.color = new Color(.91f, .84f, .47f, alpha * .16f);
        }
    }
}
