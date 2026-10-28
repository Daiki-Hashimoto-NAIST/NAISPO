using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Naispo.Interaction;
using Naispo.Kinect;
using Naispo.Silhouette;
using Object = UnityEngine.Object;

namespace Naispo.Tests
{
    public sealed class CourtMappingTests
    {
        readonly List<Object> owned = new List<Object>();
        static readonly Vector2 Left = new Vector2(-2, -1), Right = new Vector2(2, -1);

        [TearDown]
        public void Cleanup()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                if (owned[i] == null) continue;
                if (owned[i] is RenderTexture target) target.Release();
                Object.DestroyImmediate(owned[i]);
            }
            owned.Clear();
        }

        [TestCase(-1, false, 320, 288)]
        [TestCase(-1, true, 320, 288)]
        [TestCase(1, false, 320, 288)]
        [TestCase(1, true, 320, 288)]
        [TestCase(-1, false, 640, 576)]
        [TestCase(-1, true, 640, 576)]
        [TestCase(1, false, 512, 512)]
        [TestCase(1, true, 512, 512)]
        public void CentralImageBandMapsToOwnCourtWithUniformScaleAndBottomAnchor(int side, bool mirror, int width, int height)
        {
            var mapper = Mapper(mirror);
            const float centerX = 2.75f, halfWidth = 8.25f;
            mapper.ConfigureDualCourt(true, side, centerX, halfWidth, .85f, .04f);
            mapper.Refresh(width, height);
            float low = side < 0 ? centerX - halfWidth : centerX;
            float high = side < 0 ? centerX : centerX + halfWidth;
            Vector2 first = mapper.PixelToWorld(new Vector2(width * .075f, height * .5f));
            Vector2 last = mapper.PixelToWorld(new Vector2(width * .925f, height * .5f));
            Assert.That(first.x, Is.EqualTo(mirror ? high : low).Within(.0001f));
            Assert.That(last.x, Is.EqualTo(mirror ? low : high).Within(.0001f));
            float fullLeft = mapper.PixelToWorld(Vector2.zero).x;
            float fullRight = mapper.PixelToWorld(new Vector2(width, 0)).x;
            Assert.That(Mathf.Min(fullLeft, fullRight), Is.LessThan(low - .1f));
            Assert.That(Mathf.Max(fullLeft, fullRight), Is.GreaterThan(high + .1f),
                "Both image margins must remain tracked beyond the playable court.");
            float bottom = mapper.targetCamera.transform.position.y + (.04f - .5f) * 2 * mapper.targetCamera.orthographicSize;
            Assert.That(mapper.PixelToWorld(new Vector2(width * .5f, height)).y, Is.EqualTo(bottom).Within(.0001f));
            Assert.That(mapper.UnitsPerPixel, Is.EqualTo(halfWidth / (.85f * width)).Within(.000001f));

            foreach (Vector2 pixel in new[] { Vector2.zero, new Vector2(width, height), new Vector2(64.5f, 100.5f), new Vector2(-15, height + 20) })
            {
                Vector2 world = mapper.PixelToWorld(pixel);
                Assert.That(Vector2.Distance(mapper.WorldToPixel(world), pixel), Is.LessThan(.001f),
                    "Overflow coordinates must stay invertible instead of being clamped.");
                float dx = Vector2.Distance(world, mapper.PixelToWorld(pixel + Vector2.right));
                float dy = Vector2.Distance(world, mapper.PixelToWorld(pixel + Vector2.up));
                Assert.That(dx, Is.EqualTo(dy).Within(.00001f), "Camera margins must not stretch the player.");
            }
        }

        [Test]
        public void CalibrationRevisionChangesOnlyWhenConfigurationOrMappingChanges()
        {
            var mapper = Mapper(false);
            mapper.ConfigureDualCourt(true, -1, 2, 7);
            mapper.Refresh(320, 288);
            int revision = mapper.Revision;
            Assert.That(mapper.Refresh(320, 288), Is.False);
            mapper.ConfigureDualCourt(true, -1, 2, 7);
            Assert.That(mapper.Revision, Is.EqualTo(revision));
            mapper.ConfigureDualCourt(true, -1, 2.5f, 7);
            Assert.That(mapper.Revision, Is.GreaterThan(revision));
            revision = mapper.Revision;
            mapper.settings.mirrorX = true;
            Assert.That(mapper.Refresh(320, 288), Is.True);
            Assert.That(mapper.Revision, Is.GreaterThan(revision));
            revision = mapper.Revision;
            mapper.ConfigureDualCourt(true, -1, 2.5f, 7, .9f);
            Assert.That(mapper.Revision, Is.GreaterThan(revision));
            revision = mapper.Revision;
            mapper.ConfigureDualCourt(true, -1, 2.5f, 7, .9f, .08f);
            Assert.That(mapper.Revision, Is.GreaterThan(revision));
            Assert.That(mapper.PixelToWorld(new Vector2(160, 288)).y, Is.EqualTo(-1.2f).Within(.0001f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LegacyMapperRetainsAspectFitWhenDualCourtIsDisabled(bool mirror)
        {
            var mapper = Mapper(mirror);
            mapper.viewport = new Rect(.5f, .04f, .5f, .72f);
            mapper.ConfigureDualCourt(false, 1, 100, 30, .6f);
            mapper.Refresh(320, 288);
            Vector2 cameraSize = new Vector2(10 * mapper.targetCamera.aspect, 10);
            Vector2 expectedCenter = new Vector2(2, 3) + new Vector2(.25f * cameraSize.x, -.10f * cameraSize.y);
            Vector2 expectedSize = Vector2.Scale(cameraSize, new Vector2(.5f, .72f));
            float scale = Mathf.Min(expectedSize.x / 320, expectedSize.y / 288);
            Assert.That(Vector2.Distance(mapper.Center, expectedCenter), Is.LessThan(.0001f));
            Assert.That(Vector2.Distance(mapper.ViewSize, expectedSize), Is.LessThan(.0001f));
            Assert.That(mapper.UnitsPerPixel, Is.EqualTo(scale).Within(.000001f));
            Vector2 expectedTopLeft = expectedCenter + new Vector2((mirror ? 160 : -160) * scale, 144 * scale);
            Assert.That(Vector2.Distance(mapper.PixelToWorld(Vector2.zero), expectedTopLeft), Is.LessThan(.0001f));
        }

        [TestCase(-1, 0f)]
        [TestCase(1, 0f)]
        [TestCase(-1, 3.7f)]
        [TestCase(1, 3.7f)]
        public void FadeKeepsOwnCourtOpaqueAndTreatsBothOverflowEdgesSymmetrically(int side, float center)
        {
            const float halfWidth = 7;
            foreach (float inward in new[] { 0f, halfWidth * .5f, halfWidth })
                Assert.That(CourtEdgeFade.Alpha(center + side * inward, center, halfWidth, side), Is.EqualTo(1).Within(.00001f));
            foreach (float overflow in new[] { CourtEdgeFade.Width * .5f, CourtEdgeFade.Width, CourtEdgeFade.Width * 2 })
            {
                float expected = overflow < CourtEdgeFade.Width ? .5f : 0;
                Assert.That(CourtEdgeFade.Alpha(center - side * overflow, center, halfWidth, side), Is.EqualTo(expected).Within(.00001f));
                Assert.That(CourtEdgeFade.Alpha(center + side * (halfWidth + overflow), center, halfWidth, side), Is.EqualTo(expected).Within(.00001f));
            }
            Assert.That(CourtEdgeFade.Alpha(center + 100, center, halfWidth, 0), Is.EqualTo(1));
        }

        [TestCase(-1, false)]
        [TestCase(-1, true)]
        [TestCase(1, false)]
        [TestCase(1, true)]
        public void ProductionShaderFadesWorldSpaceAndPreservesTextureAndOptionalVertexAlpha(int side, bool vertexTint)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
                Assert.Ignore("Court fade readback requires a graphics device and floating point targets.");
            var shader = Shader.Find("Naispo/SilhouetteUnlit");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            var material = Own(new Material(shader));
            Color paint = new Color(.8f, .6f, .4f, .75f), tint = new Color(.5f, .25f, 1, .4f);
            Color background = new Color(.05f, .075f, .1f, .2f);
            var texture = Own(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true));
            texture.SetPixel(0, 0, paint); texture.Apply();
            material.mainTexture = texture;
            material.SetFloat("_UseVertexColor", vertexTint ? 1 : 0);
            CourtEdgeFade.Configure(material, .5f, .2f, side);
            var mesh = Own(new Mesh());
            // Reversed UVs and translated local vertices ensure fade uses world X.
            mesh.vertices = new[] { new Vector3(-2, 3, 0), new Vector3(-1, 3, 0), new Vector3(-1, 4, 0), new Vector3(-2, 4, 0) };
            mesh.uv = new[] { Vector2.right, Vector2.zero, Vector2.up, Vector2.one };
            mesh.colors = new[] { tint, tint, tint, tint };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            const int width = 1024, height = 16;
            var target = Own(new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
            Assert.That(target.Create(), Is.True);
            var readback = Own(new Texture2D(width, height, TextureFormat.RGBAFloat, false, true));
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                RenderTexture.active = target;
                GL.sRGBWrite = false;
                GL.Clear(true, true, background);
                GL.PushMatrix();
                try
                {
                    GL.LoadOrtho();
                    Assert.That(material.SetPass(0), Is.True);
                    Graphics.DrawMeshNow(mesh, Matrix4x4.Translate(new Vector3(2, -3, 0)));
                }
                finally { GL.PopMatrix(); }
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
            Color body = vertexTint ? paint * tint : paint;
            foreach (float worldX in new[] { .5f + side * .1f, .5f - side * .225f, .5f - side * .46f, .5f + side * .425f })
            {
                int x = Mathf.FloorToInt(worldX * width);
                float sampledX = (x + .5f) / width;
                float alpha = body.a * CourtEdgeFade.Alpha(sampledX, .5f, .2f, side);
                Color expected = new Color(body.r * alpha + background.r * (1 - alpha),
                    body.g * alpha + background.g * (1 - alpha), body.b * alpha + background.b * (1 - alpha),
                    alpha * alpha + background.a * (1 - alpha));
                Color actual = readback.GetPixel(x, height / 2);
                Assert.That(actual.r, Is.EqualTo(expected.r).Within(.004f), "Red at world X=" + sampledX);
                Assert.That(actual.g, Is.EqualTo(expected.g).Within(.004f), "Green at world X=" + sampledX);
                Assert.That(actual.b, Is.EqualTo(expected.b).Within(.004f), "Blue at world X=" + sampledX);
                Assert.That(actual.a, Is.EqualTo(expected.a).Within(.004f), "Alpha blending at world X=" + sampledX);
            }
        }

        [Test]
        public void FourTenthsGraceExpiresFromLastReceiptAndCachedSamplesCannotExtendIt()
        {
            var pose = new HandPoseStabilizer();
            Observe(pose, 1, true, 0, 0);
            Observe(pose, 2, true, .2, .2);
            Assert.That(pose.Ready, Is.True);
            Observe(pose, 3, false, .25, .25);
            Assert.That(pose.Holding, Is.True);
            Observe(pose, 3, true, .599, .599);
            Assert.That(pose.Ready && pose.Holding, Is.True,
                "A reused revision cannot replace the invalid sample or restart the receipt timer.");
            Assert.That(pose.Left, Is.EqualTo(Left));
            Assert.That(pose.Right, Is.EqualTo(Right));
            pose.Advance(.2 + .399, .4f);
            Assert.That(pose.Ready, Is.True);
            pose.Advance(.2 + .401, .4f);
            Assert.That(pose.Ready || pose.Holding, Is.False);
        }

        [Test]
        public void RecoveryAfterExtendedLossNeedsFreshSamplesSpanningTwoTenths()
        {
            var pose = new HandPoseStabilizer();
            Observe(pose, 1, true, 0, 0);
            Observe(pose, 2, true, .2, .2);
            pose.Advance(.601, .4f);
            Assert.That(pose.Ready, Is.False);
            Observe(pose, 3, true, 1, 1);
            Observe(pose, 3, true, 1.2, 1.2);
            Assert.That(pose.Ready, Is.False, "Elapsed time on one cached recovery frame must not restart play.");
            Observe(pose, 4, true, 1.199, 1.201);
            Assert.That(pose.Ready, Is.False);
            Observe(pose, 5, true, 1.201, 1.202);
            Assert.That(pose.Ready, Is.True);
        }

        static void Observe(HandPoseStabilizer pose, long revision, bool valid, double receivedAt, double now) =>
            pose.Observe(revision, 7, valid, Left, Right, 1.2f, receivedAt, now, .4f, .2f);

        KinectViewportMapper Mapper(bool mirror)
        {
            var go = Own(new GameObject("Court mapping test"));
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 5; camera.aspect = 16f / 9f;
            camera.transform.position = new Vector3(2, 3, -10);
            var mapper = go.AddComponent<KinectViewportMapper>();
            mapper.targetCamera = camera;
            mapper.settings = Own(ScriptableObject.CreateInstance<KinectSettings>());
            mapper.settings.mirrorX = mirror;
            return mapper;
        }

        T Own<T>(T value) where T : Object
        {
            if (value != null) owned.Add(value);
            return value;
        }
    }
}
