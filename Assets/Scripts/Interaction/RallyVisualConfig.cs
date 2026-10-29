using UnityEngine;

namespace Naispo.Interaction
{
    /// <summary>
    /// Optional presentation assets and restrained palettes for the cooperative rally.
    /// Every sprite slot may stay empty; the render-only worlds then use procedural art.
    /// Imported sprites and textures are never owned or destroyed by the game.
    /// </summary>
    [CreateAssetMenu(fileName = "RallyVisualConfig", menuName = "Naispo/Rally Visual Config")]
    public sealed class RallyVisualConfig : ScriptableObject
    {
        [Header("Midnight glasshouse palette")]
        public Color gardenDeep = new Color(.012f, .043f, .055f, 1);
        public Color gardenGlass = new Color(.10f, .34f, .38f, 1);
        public Color gardenGold = new Color(.78f, .64f, .34f, 1);
        public Color gardenFoliage = new Color(.13f, .31f, .24f, 1);
        public Color gardenCandle = new Color(1f, .79f, .43f, 1);

        [Header("Violet dueling hall palette")]
        public Color battleDeep = new Color(.043f, .027f, .074f, 1);
        public Color battleGlass = new Color(.27f, .19f, .43f, 1);
        public Color battleBronze = new Color(.63f, .43f, .24f, 1);
        public Color battleViolet = new Color(.53f, .33f, .74f, 1);

        [Header("Shared identity and ball")]
        public Color ballIvory = new Color(1f, .995f, .96f, 1);
        public Color ballGold = new Color(.95f, .79f, .46f, 1);
        public Color playerJade = new Color(.64f, .94f, .80f, 1);
        public Color playerGold = new Color(.94f, .76f, .49f, 1);

        [Header("Optional collection replacements")]
        public Sprite collectionBackground;
        public Sprite battleBackground;
        [Tooltip("Depth layout authored for the glasshouse painting. Choose Uniform for unrelated replacement art.")]
        public RallyBackdropDepthProfile collectionBackgroundDepth;
        [Tooltip("Depth layout authored for the dueling-hall painting. Choose Uniform for unrelated replacement art.")]
        public RallyBackdropDepthProfile battleBackgroundDepth;
        [Tooltip("Animate candle positions traced for MidnightGlasshouse. Disable when replacing that painting.")]
        public bool animateGlasshouseCandles;
        [Tooltip("Vertical crop anchor for background art: 0 keeps the bottom, 1 keeps the top.")]
        [Range(0, 1)] public float backgroundVerticalAlignment = .5f;

        [Header("Painted background readability")]
        [Tooltip("Maximum Gaussian sigma at the farthest depth, in pixels at a 1280-pixel image width. Nearer surfaces use smaller radii.")]
        [Range(0, 6)] public float backgroundBlurPixels = 2.4f;
        [Range(0, 1)] public float backgroundSaturation = .84f;
        [Range(.2f, 1)] public float backgroundBrightness = .8f;
        [Tooltip("Reduces bright painted highlights without dimming gameplay sprites or changing the ball light.")]
        [Range(0, 1)] public float backgroundHighlightCompression = .65f;

        [Header("Glasshouse night lighting")]
        [Tooltip("Ambient exposure for the collection room only. Gameplay lights and the dueling hall keep their own brightness.")]
        [Range(.05f, 1)] public float gardenAmbientExposure = .22f;
        [Tooltip("Exposure at traced candle flames in the glasshouse painting. Nearby candlelight uses a softer falloff.")]
        [Range(1, 2)] public float gardenCandleExposure = 1.35f;

        [Header("Optional foreground replacements")]
        public Sprite ball;
        [Tooltip("Legacy star artwork. The hanging-flower game uses Bud and Flower instead.")]
        public Sprite star;
        [Tooltip("Closed hanging bud, centered with the vine joining from above. Transparent background.")]
        public Sprite bud;
        [Tooltip("Open light flower, centered to match the bud. Transparent background; keep glow compact.")]
        public Sprite flower;

        [Header("Optional spell icons")]
        public Sprite fireballIcon;
        public Sprite timerIcon;
        public Sprite lightIcon;

        public Sprite SpellIcon(RallySpell spell)
        {
            if (spell == RallySpell.Fireball) return fireballIcon;
            if (spell == RallySpell.Timer) return timerIcon;
            if (spell == RallySpell.Light) return lightIcon;
            return null;
        }

        public static RallyVisualConfig LoadDefault()
        {
            return Resources.Load<RallyVisualConfig>("RallyVisualConfig");
        }
    }
}
