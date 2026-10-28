using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Naispo.Interaction;
using Naispo.Kinect;

namespace Naispo.Tests
{
    // Real scene captures also exercise the demo -> frame -> mapper -> barrier path.
    public sealed class CooperativeCourtPreviewTests
    {
        CooperativeRallyApp app;
        bool opened;

        [UnityTest]
        public IEnumerator TrackedHandsFullyBeyondCenterDoNotPauseAndRecoverAfterRealLoss()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/CooperativeCoinRally.unity", OpenSceneMode.Single);
            opened = true;
            yield return new EnterPlayMode();
            app = UnityEngine.Object.FindFirstObjectByType<CooperativeRallyApp>();
            app.UseSensors();
            app.Sensors.StopWorkers();
            app.Sensors.enabled = false;
            var sources = new CourtMarginTrackingSource[2];
            for(int slot=0;slot<2;slot++)
            {
                var player=app.Players[slot];
                player.enabled=false;player.source.enabled=false;
                sources[slot]=player.gameObject.AddComponent<CourtMarginTrackingSource>();
                sources[slot].Identity=(uint)(980+slot);
                // Sensors are mirrored; both hands remain inside the actual camera
                // image while their mapped positions lie wholly in opponent space.
                sources[slot].FirstX=slot==0 ? 5 : 306;
                player.source=sources[slot];player.enabled=true;
            }
            yield return WaitFor(() => app.BothPlayersReady && app.Game.InputReady,4);
            yield return new WaitForFixedUpdate();
            foreach(var player in app.Players)
            {
                var controller=player.GetComponent<HandWallController>();
                Assert.That(controller.TrackingReady,Is.True);
                Assert.That(controller.trackingGraceSeconds,Is.EqualTo(.4f));
                foreach(var collider in controller.Wall(0).GetComponents<EdgeCollider2D>())
                    Assert.That(collider.enabled,Is.False,"Opponent overflow must have no physical geometry.");
                Assert.That(controller.Wall(0).GetComponent<LineRenderer>().enabled,Is.True,
                    "The shader owns visual fading; losing collision geometry is not tracking loss.");
            }
            var first=app.Players[0].GetComponent<HandWallController>();
            sources[0].Valid=false;
            yield return WaitFor(() => first.HoldingLastPose,1);
            Assert.That(first.TrackingReady && app.Game.InputReady,Is.True);
            yield return WaitFor(() => !first.TrackingReady && !app.Game.InputReady,2);
            sources[0].Valid=true;
            yield return WaitFor(() => first.TrackingReady && app.Game.InputReady,3);
            Assert.That(first.Wall(0).enabled,Is.False,"Reacquisition must not depend on a collider re-entering the court.");
        }

        static IEnumerator WaitFor(Func<bool> predicate,float timeout)
        {
            double end=Time.realtimeSinceStartupAsDouble+timeout;
            while(!predicate()&&Time.realtimeSinceStartupAsDouble<end)yield return null;
            Assert.That(predicate(),Is.True,"Timed out waiting for the real tracking/game readiness transition.");
        }

        [UnityTest]
        public IEnumerator BothThemesKeepLiveHandsAtTheNetAndOuterMargins()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Court previews need a graphics device.");
            EditorSceneManager.OpenScene("Assets/Scenes/CooperativeCoinRally.unity", OpenSceneMode.Single);
            opened = true;
            yield return new EnterPlayMode();
            app = UnityEngine.Object.FindFirstObjectByType<CooperativeRallyApp>();
            Assert.That(app, Is.Not.Null);
            foreach (var mode in new[] { RallyMode.StarCollection, RallyMode.Versus })
            {
                app.Game.SetMode(mode);
                app.Game.SetPaused(true);
                foreach (bool outer in new[] { false, true })
                {
                    long leftRevision = app.Players[0].FrameRevision;
                    long rightRevision = app.Players[1].FrameRevision;
                    for (int slot = 0; slot < 2; slot++)
                    {
                        var mapper = app.Players[slot].mapper;
                        var demo = (CooperativeDemoSource)app.Players[slot].source;
                        demo.acceptControls = false;
                        demo.angle = 0;
                        demo.span = 112;
                        int side = slot == 0 ? -1 : 1;
                        float endpoint = app.gameCamera.transform.position.x +
                            (outer ? side * (app.Game.HalfWidth + .28f) : -side * .28f);
                        float middle = endpoint + (outer ? -side : side) * demo.span * mapper.UnitsPerPixel * .5f;
                        var pixel = mapper.WorldToPixel(new Vector2(middle, -.65f));
                        demo.horizontal = pixel.x - .5f;
                        demo.vertical = pixel.y - .5f;
                    }
                    double deadline = Time.realtimeSinceStartupAsDouble + 4;
                    while ((app.Players[0].FrameRevision < leftRevision + 3 ||
                            app.Players[1].FrameRevision < rightRevision + 3) && Time.realtimeSinceStartupAsDouble < deadline)
                        yield return null;
                    yield return new WaitForFixedUpdate();
                    yield return null;
                    Assert.That(app.BothPlayersReady, Is.True, "Reaching past a virtual edge must retain valid hand input.");
                    for (int slot = 0; slot < 2; slot++)
                    {
                        var player = app.Players[slot];
                        Assert.That(HandWallController.TryEndpoints(player.CurrentFrame.Hands[0], player.mapper,
                            out var left, out var right), Is.True);
                        float min = Mathf.Min(left.x, right.x), max = Mathf.Max(left.x, right.x);
                        float center = app.gameCamera.transform.position.x;
                        Assert.That(slot == 0 ? min < center : max > center, Is.True);
                        Assert.That(outer ? (slot == 0 ? min < center - app.Game.HalfWidth : max > center + app.Game.HalfWidth)
                            : (slot == 0 ? max > center : min < center), Is.True,
                            "The capture must actually exercise overflow, not a pose clamped inside the court.");
                    }
                    SaveCamera("Logs/edge-" + (mode == RallyMode.Versus ? "battle" : "garden") + (outer ? "-outer.png" : "-center.png"));
                }
            }
        }

        void SaveCamera(string path)
        {
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var camera = app.gameCamera;
            Rect previousRect = camera.rect;
            float previousAspect = camera.aspect;
            try
            {
                target.Create();
                camera.rect = new Rect(0, 0, 1, 1);
                camera.aspect = 16f / 9;
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                pixels.Apply();
                Directory.CreateDirectory("Logs");
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                camera.rect = previousRect;
                camera.aspect = previousAspect;
                RenderTexture.active = previous;
                target.Release();
                UnityEngine.Object.Destroy(pixels);
                UnityEngine.Object.Destroy(target);
            }
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (!opened) yield break;
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            opened = false;
        }
    }

    public sealed class CourtMarginTrackingSource : BodyTrackingSource
    {
        public uint Identity=980;
        public float FirstX=5;
        public bool Valid=true;
        long sequence;
        public override string Status => "Court margin test";
        public override float CaptureFps => 30;
        public override bool WorkerAlive => isActiveAndEnabled;
        public override bool HasFailed => false;
        public override bool TryGetLatest(ref BodyFrameData frame)
        {
            frame.EnsureSize(320,288);
            frame.Sequence=++sequence;frame.TimestampSeconds=Time.unscaledTimeAsDouble;
            frame.BodyCount=1;frame.BodyIds[0]=Identity;
            frame.Hands[0]=new BodyFrameData.HandPair {
                Left=new System.Numerics.Vector2(FirstX,200),
                Right=new System.Numerics.Vector2(FirstX+8,200),
                SeparationMeters=.35f,Valid=Valid
            };
            return true;
        }
    }
}
