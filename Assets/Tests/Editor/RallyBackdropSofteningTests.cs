using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Naispo.Interaction;
using Object = UnityEngine.Object;

namespace Naispo.Tests
{
    public sealed class RallyBackdropSofteningTests
    {
        readonly List<Object> owned = new List<Object>();

        [SetUp]
        public void RequireGraphicsReadback()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
                Assert.Ignore("Backdrop GPU tests require a graphics device, half-float render targets and float readback.");
            if (QualitySettings.activeColorSpace != ColorSpace.Linear)
                Assert.Ignore("These tests verify the game's Linear color-space pipeline.");
        }

        [TearDown]
        public void ReleaseArtwork()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                if (owned[i] == null) continue;
                if (owned[i] is RenderTexture target) target.Release();
                Object.DestroyImmediate(owned[i]);
            }
            owned.Clear();
        }

        [Test]
        public void ConstantDarkPaintKeepsItsLinearColorThroughBothBlurPasses()
        {
            const int width = 256, height = 96;
            var sourceColor = new Color32(19, 31, 47, 255);
            var pixels = Filled(width * height, sourceColor);
            var blurred = Bake(pixels, width, height, new Rect(0, 0, width, height));
            var colors = Readback(blurred);
            Color expected = ((Color)sourceColor).linear;

            // A gamma round trip or an 8-bit linear intermediate visibly changes
            // these dark values, even though the input is a uniform painted area.
            AssertColor(colors[(height / 2) * width + width / 2], expected, .00025f);
            AssertColor(colors[0], expected, .00025f);
        }

        [Test]
        public void CroppedSpriteNeverSamplesBrightPaintOutsideItsSelectedRect()
        {
            const int width = 320, height = 160;
            var crop = new RectInt(27, 19, 256, 112);
            var inside = new Color32(32, 91, 131, 255);
            var pixels = Filled(width * height, new Color32(255, 0, 255, 255));
            for (int y = crop.yMin; y < crop.yMax; y++)
                for (int x = crop.xMin; x < crop.xMax; x++)
                    pixels[y * width + x] = inside;
            var blurred = Bake(pixels, width, height,
                new Rect(crop.x, crop.y, crop.width, crop.height));
            var colors = Readback(blurred);
            Color expected = ((Color)inside).linear;

            foreach (int y in new[] { crop.yMin, crop.yMax - 1 })
                foreach (int x in new[] { crop.xMin, crop.xMax - 1 })
                    AssertColor(colors[y * width + x], expected, .0005f);
        }

        [Test]
        public void TransparentHiddenColorDoesNotBleedAndAlphaIsPremultipliedOnlyOnce()
        {
            const int width = 256, height = 96;
            var pixels = Filled(width * height, new Color32(255, 0, 0, 0));
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width / 2; x++)
                    pixels[y * width + x] = new Color32(0, 255, 0, 64);
            var blurred = Bake(pixels, width, height, new Rect(0, 0, width, height));
            var colors = Readback(blurred);
            const float alpha = 64f / 255f;

            AssertColor(colors[(height / 2) * width + width / 4],
                new Color(0, alpha, 0, alpha), .0005f);
            Color edge = colors[(height / 2) * width + width / 2 - 1];
            Assert.That(edge.a, Is.GreaterThan(.05f).And.LessThan(alpha - .01f),
                "The boundary must actually include neighboring transparent texels.");
            Assert.That(edge.r, Is.EqualTo(0).Within(.0001f), "Hidden red RGB must not enter the blur.");
            Assert.That(edge.g / edge.a, Is.EqualTo(1).Within(.002f),
                "Unpremultiplying the cached boundary must restore the visible green color.");
        }

        RenderTexture Bake(Color32[] pixels, int width, int height, Rect crop)
        {
            var source = Own(new Texture2D(width, height, TextureFormat.RGBA32, false, false));
            source.SetPixels32(pixels);
            source.Apply(false, true);
            var sprite = Own(Sprite.Create(source, crop, Vector2.one * .5f, 100, 0, SpriteMeshType.FullRect));
            var config = Own(ScriptableObject.CreateInstance<RallyVisualConfig>());
            config.backgroundBlurPixels = 6;
            var material = Own(RallySceneryArtwork.CreateBackdropMaterial(sprite, config, out var blurred));
            Own(blurred);
            Assert.That(material, Is.Not.Null, "The production backdrop shader must be supported.");
            Assert.That(blurred, Is.Not.Null, "The production Gaussian shader must create a GPU cache.");
            return blurred;
        }

        Color[] Readback(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                var texture = Own(new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true));
                RenderTexture.active = source;
                texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                return texture.GetPixels();
            }
            finally { RenderTexture.active = previous; }
        }

        static Color32[] Filled(int count, Color32 color)
        {
            var pixels = new Color32[count];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            return pixels;
        }

        static void AssertColor(Color actual, Color expected, float tolerance)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance), "Red");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance), "Green");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance), "Blue");
            // Two half-float passes can round an opaque alpha by one half ULP;
            // keep the tighter dark-color threshold for RGB, where it matters.
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(.001f), "Alpha");
        }

        T Own<T>(T value) where T : Object
        {
            if (value != null) owned.Add(value);
            return value;
        }
    }
}
