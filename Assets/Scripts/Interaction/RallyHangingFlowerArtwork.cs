using UnityEngine;

namespace Naispo.Interaction
{
    /// <summary>Small, sharp light flowers; no baked outer glow or scene lighting.</summary>
    public static class RallyHangingFlowerArtwork
    {
        public static Texture2D CreateBudTexture()
        {
            const int width = 112, height = 160;
            var texture = RallySceneryArtwork.NewTexture(width, height, "Hanging luminous bud");
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float px = (x + .5f) / width * 2 - 1;
                float py = (y + .5f) / height * 2 - 1;
                float v = Mathf.Clamp01((py + .92f) / 1.66f);
                float half = .69f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(Mathf.PI * v)), .68f) * (1 - .32f * v);
                float edge = half - Mathf.Abs(px);
                float alpha = Mathf.Clamp01(edge / .023f);
                if (py < -.92f || py > .74f) alpha = 0;
                float rim = 1 - SmoothRange(.015f, .10f, edge);
                float fold = 1 - SmoothRange(.015f, .055f,
                    Mathf.Abs(px - Mathf.Sin(v * Mathf.PI) * .13f));
                Color color = Color.Lerp(new Color(.37f, .66f, .55f),
                    new Color(.87f, .99f, .89f), Mathf.Clamp01(.30f + (1 - v) * .55f));
                color = Color.Lerp(color, new Color(.98f, 1f, .91f), Mathf.Clamp01(rim * .7f + fold * .4f));
                // A short dark calyx attaches the hanging teardrop to its vine.
                float calyx = Mathf.Clamp01((.27f - Mathf.Abs(px)) / .025f) *
                    Mathf.Clamp01((py - .57f) / .045f) * Mathf.Clamp01((.87f - py) / .04f);
                float notch = Mathf.Abs(px) > .07f ? Mathf.Clamp01((py - .73f) / .08f) : 0;
                calyx *= 1 - notch;
                if (calyx > alpha)
                {
                    alpha = calyx;
                    color = new Color(.31f, .57f, .40f);
                }
                color.a = alpha;
                pixels[y * width + x] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        public static Texture2D CreateFlowerTexture()
        {
            const int size = 160;
            var texture = RallySceneryArtwork.NewTexture(size, size, "Six petal light flower");
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 point = new Vector2((x + .5f) / size * 2 - 1,
                    (y + .5f) / size * 2 - 1);
                float radius = point.magnitude;
                Color color = Color.clear;
                // Six overlapping tapered oval petals, with folds pointing inward.
                for (int petal = 0; petal < 6; petal++)
                {
                    float angle = (petal * 60f + 30f) * Mathf.Deg2Rad;
                    Vector2 axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    float along = Vector2.Dot(point, axis);
                    float across = Vector2.Dot(point, new Vector2(-axis.y, axis.x));
                    float v = Mathf.Clamp01((along + .06f) / .98f);
                    float halfWidth = .29f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(v * Mathf.PI)), .70f);
                    float edge = halfWidth - Mathf.Abs(across);
                    float alpha = Mathf.Clamp01(edge / .019f) *
                        Mathf.Clamp01((along + .06f) / .019f) * Mathf.Clamp01((.92f - along) / .019f);
                    if (alpha <= 0) continue;
                    float rim = 1 - SmoothRange(.01f, .055f, edge);
                    float fold = (1 - SmoothRange(.006f, .028f, Mathf.Abs(across))) *
                        Mathf.Sin(v * Mathf.PI) * .24f;
                    Color petalColor = Color.Lerp(new Color(.54f, .79f, .71f),
                        new Color(.98f, 1f, .94f), Mathf.Clamp01(.15f + v * .85f + rim * .2f));
                    petalColor = Color.Lerp(petalColor, Color.white, fold);
                    if (alpha > color.a)
                    {
                        petalColor.a = alpha;
                        color = petalColor;
                    }
                }
                // Fine gold stamens surround a compact ivory center. This stays
                // legible at game scale without a broad, fuzzy halo in the asset.
                float theta = Mathf.Atan2(point.y, point.x);
                float spokes = Mathf.Pow(Mathf.Max(0, Mathf.Cos(theta * 6)), 12);
                float filament = spokes * Mathf.Clamp01((.29f - radius) / .025f) *
                    Mathf.Clamp01((radius - .09f) / .025f);
                color = Color.Lerp(color, new Color(1f, .86f, .47f, 1), filament * .9f);
                float core = Mathf.Clamp01((.115f - radius) / .018f);
                color = Color.Lerp(color, new Color(1f, .98f, .82f, 1), core);
                pixels[y * size + x] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static float SmoothRange(float start, float end, float value) =>
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(start, end, value));
    }
}
