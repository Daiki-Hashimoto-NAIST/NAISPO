using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Naispo.Interaction;
using Object = UnityEngine.Object;

namespace Naispo.Tests
{
    public sealed class RallyBackdropDepthTests
    {
        readonly List<Object> owned = new List<Object>();

        [TearDown]
        public void ReleaseArtwork()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [TestCase(RallyBackdropDepthProfile.Glasshouse)]
        [TestCase(RallyBackdropDepthProfile.DuelingHall)]
        public void ColumnsKeepTheirDepthAlongTheShaftAndRecedeByStructuralLayer(RallyBackdropDepthProfile profile)
        {
            var depth = SceneDepth(profile);
            float middleX = profile == RallyBackdropDepthProfile.Glasshouse ? .253f : .263f;
            float front = ConstantShaftDepth(depth, .15f, .25f, .50f, .78f);
            // Garden foliage legitimately covers the lower middle pilaster;
            // compare the exposed shaft instead of the nearer plant in front.
            float middle = profile == RallyBackdropDepthProfile.Glasshouse
                ? ConstantShaftDepth(depth, middleX, .42f, .45f, .48f)
                : ConstantShaftDepth(depth, middleX, .45f, .60f, .78f);
            float inner = ConstantShaftDepth(depth, .351f, .25f, .50f, .78f);
            float back = AtTop(depth, .50f, .37f);

            Assert.That(front, Is.LessThan(middle - .05f), "The front column must stay nearer than the middle pilaster.");
            Assert.That(middle, Is.LessThan(inner - .05f), "The inner column must recede behind the middle pilaster.");
            Assert.That(inner, Is.LessThan(back - .05f), "The far wall must stay behind the supporting columns.");
        }

        [TestCase(RallyBackdropDepthProfile.Glasshouse)]
        [TestCase(RallyBackdropDepthProfile.DuelingHall)]
        public void FloorRecedesTowardTheHorizonAndRoofRibsStayInFrontOfTheMoon(RallyBackdropDepthProfile profile)
        {
            var depth = SceneDepth(profile);
            float nearFloor = AtTop(depth, .50f, .98f);
            float middleFloor = AtTop(depth, .50f, .92f);
            float farFloor = AtTop(depth, .50f, .87f);
            Assert.That(nearFloor, Is.LessThan(middleFloor - .05f));
            Assert.That(middleFloor, Is.LessThan(farFloor - .05f),
                "Floor blur must grow with the painted floor's recession, not distance from the image center.");
            Assert.That(AtTop(depth, .355f, .15f), Is.LessThan(AtTop(depth, .39f, .10f) - .10f),
                "The roof rib crosses in front of distant sky, even though both occupy the upper image.");
        }

        [TestCase(RallyBackdropDepthProfile.Glasshouse)]
        [TestCase(RallyBackdropDepthProfile.DuelingHall)]
        public void NonReadableSpriteCropKeepsDepthAlignedAndSourceAssetsAlive(RallyBackdropDepthProfile profile)
        {
            const int sceneWidth = 512, sceneHeight = 320;
            var crop = new Rect(113, 71, sceneWidth, sceneHeight);
            var full = Source(sceneWidth, sceneHeight, new Rect(0, 0, sceneWidth, sceneHeight), Color.red);
            var cropped = Source(768, 512, crop, Color.green);
            var fullDepth = Own(RallyBackdropDepth.CreateTexture(full, profile));
            var croppedDepth = Own(RallyBackdropDepth.CreateTexture(cropped, profile));
            Assert.That(fullDepth, Is.Not.Null);
            Assert.That(croppedDepth, Is.Not.Null);
            Assert.That(croppedDepth.width, Is.EqualTo(cropped.texture.width));
            Assert.That(croppedDepth.height, Is.EqualTo(cropped.texture.height));
            var expected = fullDepth.GetPixels32();
            var actual = croppedDepth.GetPixels32();

            foreach (int y in new[] { 0, 31, 79, 160, 240, sceneHeight - 1 })
                foreach (int x in new[] { 0, 31, 96, 159, 255, 383, sceneWidth - 1 })
                    Assert.That(actual[((int)crop.y + y) * croppedDepth.width + (int)crop.x + x],
                        Is.EqualTo(expected[y * sceneWidth + x]),
                        $"Depth must follow sprite-local artwork at ({x}, {y}), independently of its texture offset or color.");

            Object.DestroyImmediate(fullDepth);
            Object.DestroyImmediate(croppedDepth);
            Assert.That(full != null && full.texture != null, Is.True);
            Assert.That(cropped != null && cropped.texture != null, Is.True);
            Assert.That(cropped.texture.isReadable, Is.False, "Creating depth must not change the source import/readability.");
            Assert.That(cropped.rect, Is.EqualTo(crop));
        }

        [Test]
        public void ShippedBackgroundsSelectTheirMatchingSceneDepthProfiles()
        {
            var config = RallyVisualConfig.LoadDefault();
            Assert.That(config, Is.Not.Null);
            Assert.That(config.collectionBackground, Is.Not.Null);
            Assert.That(config.battleBackground, Is.Not.Null);
            Assert.That(config.collectionBackgroundDepth, Is.EqualTo(RallyBackdropDepthProfile.Glasshouse));
            Assert.That(config.battleBackgroundDepth, Is.EqualTo(RallyBackdropDepthProfile.DuelingHall));
        }

        Texture2D SceneDepth(RallyBackdropDepthProfile profile)
        {
            var sprite = Source(640, 400, new Rect(0, 0, 640, 400), Color.black);
            var result = Own(RallyBackdropDepth.CreateTexture(sprite, profile));
            Assert.That(result, Is.Not.Null);
            return result;
        }

        Sprite Source(int width, int height, Rect crop, Color color)
        {
            var source = Own(new Texture2D(width, height, TextureFormat.RGBA32, false));
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            source.SetPixels32(pixels);
            source.Apply(false, true);
            return Own(Sprite.Create(source, crop, new Vector2(.23f, .81f), 37, 0, SpriteMeshType.FullRect));
        }

        static float ConstantShaftDepth(Texture2D depth, float x, params float[] topYs)
        {
            float first = AtTop(depth, x, topYs[0]);
            foreach (float topY in topYs)
                Assert.That(AtTop(depth, x, topY), Is.EqualTo(first).Within(.035f),
                    "A single column must not change focus between its capital and base.");
            return first;
        }

        static float AtTop(Texture2D depth, float x, float topY) =>
            depth.GetPixel(Mathf.FloorToInt(x * depth.width),
                depth.height - 1 - Mathf.FloorToInt(topY * depth.height)).r;

        T Own<T>(T value) where T : Object
        {
            if (value != null) owned.Add(value);
            return value;
        }
    }
}
