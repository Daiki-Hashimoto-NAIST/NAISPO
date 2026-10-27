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
        [Tooltip("Vertical crop anchor for background art: 0 keeps the bottom, 1 keeps the top.")]
        [Range(0, 1)] public float backgroundVerticalAlignment = .5f;

        [Header("Painted background readability")]
        [Tooltip("Background-only softening radius in screen pixels at a 1280-pixel viewport width.")]
        [Range(0, 4)] public float backgroundBlurPixels = 2f;
        [Range(0, 1)] public float backgroundSaturation = .72f;
        [Range(.2f, 1)] public float backgroundBrightness = .8f;
        [Tooltip("Reduces bright painted highlights without dimming gameplay sprites or changing the ball light.")]
        [Range(0, 1)] public float backgroundHighlightCompression = .65f;

        [Header("Optional foreground replacements")]
        public Sprite ball;
        public Sprite star;
        public Sprite bud;
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
