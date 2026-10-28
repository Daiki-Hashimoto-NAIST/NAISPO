using UnityEngine;

namespace Naispo.Interaction
{
    /// <summary>
    /// Local motion masks traced over the supplied 1586 x 992 glasshouse painting.
    /// Coordinates refer to the wick, measured from the top left of the sprite crop.
    /// The source image stays untouched; the caller owns the returned linear texture.
    /// </summary>
    public static class RallyCandleArtwork
    {
        static readonly Vector3[] Flames =
        {
            new Vector3(40, 412, 23), new Vector3(67, 429, 17),
            new Vector3(157, 394, 16), new Vector3(111, 468, 13),
            new Vector3(75, 486, 11), new Vector3(106, 488, 12), new Vector3(117, 482, 12),
            new Vector3(31, 637, 22), new Vector3(50, 662, 17),
            new Vector3(154, 564, 16), new Vector3(139, 574, 9),
            new Vector3(448, 520, 9), new Vector3(461, 514, 13),
            new Vector3(1081, 707, 11), new Vector3(1098, 698, 15),
            new Vector3(1123, 510, 13), new Vector3(1136, 520, 10),
            new Vector3(1430, 395, 15), new Vector3(1546, 412, 21),
            new Vector3(1520, 452, 16), new Vector3(1460, 483, 12), new Vector3(1474, 490, 13),
            new Vector3(1431, 565, 15), new Vector3(1446, 575, 10),
            new Vector3(1553, 639, 19), new Vector3(1534, 661, 14)
        };

        public static Texture2D CreateMotionMask(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null || sprite.packed) return null;
            int width = sprite.texture.width, height = sprite.texture.height;
            Rect crop = sprite.rect;
            var pixels = new Color32[width * height];
            var neutral = new Color32(128, 128, 128, 128);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = neutral;
            for (int i = 0; i < Flames.Length; i++)
            {
                Vector3 flame = Flames[i];
                float phase = i * 2.399963f;
                float cosine = Mathf.Cos(phase), sine = Mathf.Sin(phase);
                float radius = flame.z * 2.6f;
                int xMin = Mathf.Max(0, Mathf.FloorToInt(crop.x + (flame.x - radius) / 1586 * crop.width));
                int xMax = Mathf.Min(width - 1, Mathf.CeilToInt(crop.x + (flame.x + radius) / 1586 * crop.width));
                int yMin = Mathf.Max(0, Mathf.FloorToInt(crop.yMax - (flame.y + radius) / 992 * crop.height));
                int yMax = Mathf.Min(height - 1, Mathf.CeilToInt(crop.yMax - (flame.y - radius) / 992 * crop.height));
                for (int y = yMin; y <= yMax; y++)
                for (int x = xMin; x <= xMax; x++)
                {
                    float dx = ((x + .5f - crop.x) / crop.width * 1586 - flame.x) / flame.z;
                    float up = (flame.y - (crop.yMax - y - .5f) / crop.height * 992) / flame.z;
                    // Pin the wick and wax. Only the flame above it bends and stretches.
                    float bend = Mathf.SmoothStep(0, 1, Mathf.Clamp01(up)) *
                        Mathf.SmoothStep(0, 1, Mathf.Clamp01((1.55f - up) / .55f)) *
                        Mathf.Exp(-dx * dx * 8);
                    float distance = Mathf.Sqrt(dx * dx + (up - .45f) * (up - .45f));
                    float glow = Mathf.Pow(Mathf.Clamp01(1 - distance / 2.25f), 2);
                    int index = y * width + x;
                    Color32 previous = pixels[index];
                    // Store weighted cosine/sine pairs for motion and brightness.
                    // Add neighbouring fields continuously rather than switching
                    // to another candle's phase across an overlapping flame tip.
                    float motion = bend * flame.z / 32;
                    pixels[index] = new Color32(
                        AddSigned(previous.r, motion * cosine), AddSigned(previous.g, motion * sine),
                        AddSigned(previous.b, glow * cosine * .5f), AddSigned(previous.a, glow * sine * .5f));
                }
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = "Glasshouse candle motion mask",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static byte AddSigned(byte previous, float value) => (byte)Mathf.Clamp(
            Mathf.RoundToInt(previous + value * 127), 1, 255);

        public static Vector4 MotionScale(Sprite sprite) => new Vector4(
            32f * sprite.rect.width / (1586 * sprite.texture.width),
            32f * sprite.rect.height / (992 * sprite.texture.height), 0, 0);
    }
}
