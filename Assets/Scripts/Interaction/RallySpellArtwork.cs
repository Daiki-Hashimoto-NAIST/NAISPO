using System.Collections.Generic;
using UnityEngine;

namespace Naispo.Interaction
{
    /// <summary>Small, bold spell silhouettes shared by pickups, the ball and the HUD.</summary>
    public static class RallySpellArtwork
    {
        public const float WorldSize = .62f;
        static readonly Color Keyline = new Color(.022f, .018f, .043f, .94f);
        static readonly Color Ivory = new Color(1f, .97f, .79f, 1);

        public static Color ColorFor(RallySpell spell)
        {
            if (spell == RallySpell.Fireball) return new Color(1, .55f, .15f);
            if (spell == RallySpell.Timer) return new Color(.40f, .88f, 1);
            if (spell == RallySpell.Light) return new Color(.84f, .63f, 1);
            return Color.white;
        }

        // The caller owns the returned texture. Geometry is sampled once on creation;
        // a one-texel distance ramp keeps the silhouettes smooth when displayed small.
        public static Texture2D Create(RallySpell spell, int size = 128)
        {
            if (spell == RallySpell.None || (int)spell < 0 || (int)spell > 3)
                throw new System.ArgumentOutOfRangeException(nameof(spell));
            if (size < 32) throw new System.ArgumentOutOfRangeException(nameof(size));
            Vector2[] flame = spell == RallySpell.Fireball ? FlameShape(false) : null;
            Vector2[] core = spell == RallySpell.Fireball ? FlameShape(true) : null;
            var pixels = new Color32[size * size];
            Color ink = ColorFor(spell);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector2 point = new Vector2((x + .5f) / size - .5f, (y + .5f) / size - .5f);
                    Color color = Color.clear;
                    if (spell == RallySpell.Fireball)
                    {
                        float distance = PolygonDistance(point, flame);
                        Paint(ref color, Keyline, distance - .021f, size);
                        Paint(ref color, ink, distance, size);
                        Paint(ref color, Ivory, PolygonDistance(point, core), size);
                    }
                    else if (spell == RallySpell.Timer)
                        PaintClock(ref color, point, ink, size);
                    else
                        PaintInvisibleEye(ref color, point, ink, size);
                    pixels[y * size + x] = color;
                }
            var texture = RallySceneryArtwork.NewTexture(size, size, "Rally " + spell + " symbol");
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        static void PaintClock(ref Color color, Vector2 point, Color ink, int size)
        {
            Vector2 center = new Vector2(0, -.055f);
            float dial = (point - center).magnitude - .32f;
            float stem = BoxDistance(point - new Vector2(0, .31f), new Vector2(.045f, .075f));
            float crown = BoxDistance(point - new Vector2(0, .40f), new Vector2(.11f, .042f));
            float button = SegmentDistance(point, new Vector2(.235f, .22f), new Vector2(.29f, .28f)) - .035f;
            float body = Mathf.Min(dial, Mathf.Min(stem, Mathf.Min(crown, button)));
            Paint(ref color, Keyline, body - .021f, size);
            Paint(ref color, ink, body, size);
            // The quiet dark face separates the two hands from a detailed background.
            Paint(ref color, new Color(.025f, .065f, .095f, 1), (point - center).magnitude - .249f, size);
            float hands = Mathf.Min(SegmentDistance(point, center, center + new Vector2(0, .175f)),
                SegmentDistance(point, center, center + new Vector2(.13f, 0))) - .028f;
            Paint(ref color, Ivory, hands, size);
            Paint(ref color, Ivory, (point - center).magnitude - .043f, size);
        }

        static void PaintInvisibleEye(ref Color color, Vector2 point, Color ink, int size)
        {
            // The intersection of two circles forms a broad almond, readable without
            // tiny eyelashes. An offset slash has its own keyline across the pupil.
            float outer = LensDistance(point, .45f, .24f);
            float inner = LensDistance(point, .37f, .22f);
            float rim = Mathf.Max(outer, -inner);
            float pupil = point.magnitude - .105f;
            float eye = Mathf.Min(rim, pupil);
            Paint(ref color, Keyline, eye - .021f, size);
            Paint(ref color, ink, eye, size);
            float slash = SegmentDistance(point, new Vector2(-.32f, -.30f), new Vector2(.32f, .30f)) - .034f;
            Paint(ref color, Keyline, slash - .025f, size);
            Paint(ref color, new Color(.97f, .88f, 1, 1), slash, size);
        }

        static float LensDistance(Vector2 point, float radius, float offset)
            => Mathf.Max((point - new Vector2(0, offset)).magnitude,
                (point + new Vector2(0, offset)).magnitude) - radius;

        static float BoxDistance(Vector2 point, Vector2 halfSize)
        {
            Vector2 delta = new Vector2(Mathf.Abs(point.x), Mathf.Abs(point.y)) - halfSize;
            return new Vector2(Mathf.Max(0, delta.x), Mathf.Max(0, delta.y)).magnitude
                + Mathf.Min(Mathf.Max(delta.x, delta.y), 0);
        }

        static float SegmentDistance(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 delta = end - start;
            float t = Mathf.Clamp01(Vector2.Dot(point - start, delta) / delta.sqrMagnitude);
            return (point - start - delta * t).magnitude;
        }

        static float PolygonDistance(Vector2 point, Vector2[] shape)
        {
            bool inside = false;
            float minimum = float.PositiveInfinity;
            for (int i = 0, previous = shape.Length - 1; i < shape.Length; previous = i++)
            {
                Vector2 a = shape[previous], b = shape[i], delta = b - a;
                float t = delta.sqrMagnitude > .000001f
                    ? Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude) : 0;
                minimum = Mathf.Min(minimum, (point - a - delta * t).sqrMagnitude);
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return Mathf.Sqrt(minimum) * (inside ? -1 : 1);
        }

        static Vector2[] FlameShape(bool inner)
        {
            var points = new List<Vector2>();
            if (inner)
            {
                points.Add(new Vector2(.025f, .10f));
                Curve(points, new Vector2(.025f, -.03f), new Vector2(.17f, -.10f), new Vector2(.13f, -.25f));
                Curve(points, new Vector2(.10f, -.37f), new Vector2(-.11f, -.37f), new Vector2(-.13f, -.24f));
                Curve(points, new Vector2(-.16f, -.12f), new Vector2(-.04f, -.08f), new Vector2(.025f, .10f));
            }
            else
            {
                points.Add(new Vector2(.09f, .44f));
                Curve(points, new Vector2(.02f, .24f), new Vector2(.26f, .22f), new Vector2(.25f, -.01f));
                Curve(points, new Vector2(.31f, .02f), new Vector2(.33f, .10f), new Vector2(.32f, .15f));
                Curve(points, new Vector2(.49f, -.10f), new Vector2(.32f, -.41f), new Vector2(.03f, -.43f));
                Curve(points, new Vector2(-.25f, -.46f), new Vector2(-.43f, -.25f), new Vector2(-.31f, -.03f));
                Curve(points, new Vector2(-.28f, .03f), new Vector2(-.22f, .10f), new Vector2(-.23f, .20f));
                Curve(points, new Vector2(-.17f, .15f), new Vector2(-.11f, .08f), new Vector2(-.12f, -.01f));
                Curve(points, new Vector2(-.02f, .13f), new Vector2(-.08f, .27f), new Vector2(.09f, .44f));
            }
            return points.ToArray();
        }

        static void Curve(List<Vector2> points, Vector2 control1, Vector2 control2, Vector2 end)
        {
            Vector2 start = points[points.Count - 1];
            const int segments = 10;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments, inverse = 1 - t;
                points.Add(inverse * inverse * inverse * start + 3 * inverse * inverse * t * control1
                    + 3 * inverse * t * t * control2 + t * t * t * end);
            }
        }

        static void Paint(ref Color target, Color ink, float distance, int size)
        {
            float alpha = Mathf.Clamp01(.5f - distance * size) * ink.a;
            if (alpha <= 0) return;
            float combined = alpha + target.a * (1 - alpha);
            Color mixed = (ink * alpha + target * (target.a * (1 - alpha))) / combined;
            mixed.a = combined;
            target = mixed;
        }
    }
}
