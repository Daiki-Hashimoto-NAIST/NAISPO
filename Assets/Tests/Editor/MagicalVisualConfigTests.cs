using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Naispo.Interaction;
using Object = UnityEngine.Object;

namespace Naispo.Tests
{
    /// <summary>
    /// EditMode checks of displayed geometry and resource ownership. The fake imports
    /// use cropped, non-square, non-readable textures with deliberately off-center
    /// pivots; none require changing a production API or a user's imported asset.
    /// </summary>
    public sealed class MagicalVisualConfigTests
    {
        const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        readonly List<Object> fixtureAssets = new List<Object>();
        readonly List<Object> generatedAssets = new List<Object>();
        Scene scene;
        GameObject root;
        Camera camera;

        sealed class Court
        {
            public GameObject Owner;
            public CooperativeCoinGame Game;
            public AirHockeyBall Ball;
            public Sprite OriginalBall;
            public List<Object> Generated;
        }

        [SetUp]
        public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Magical visual configuration tests");
            SceneManager.MoveGameObjectToScene(root, scene);
            camera = Create("Offset orthographic camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.aspect = 16f / 9f;
            camera.transform.position = new Vector3(6, 2, -10);
        }

        [TearDown]
        public void Cleanup()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            // Also clean up failed assertions' leaked test resources, so one failing
            // ownership regression cannot contaminate the following fixture.
            for (int i = generatedAssets.Count - 1; i >= 0; i--)
                if (generatedAssets[i] != null) Object.DestroyImmediate(generatedAssets[i]);
            for (int i = fixtureAssets.Count - 1; i >= 0; i--)
                if (fixtureAssets[i] != null) Object.DestroyImmediate(fixtureAssets[i]);
            generatedAssets.Clear();
            fixtureAssets.Clear();
            root = null;
            camera = null;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BackgroundAndCenterVeilCoverTheViewportAfterCameraAndAspectChanges(bool importedBackgrounds)
        {
            var config = Configuration(importedBackgrounds, 53);
            var court = CreateCourt(config);
            var garden = court.Game.GardenPresentation;
            var battle = court.Game.CyberPresentation;
            Assert.That(garden.UsesConfiguredBackground, Is.EqualTo(importedBackgrounds));
            Assert.That(battle.UsesConfiguredBackground, Is.EqualTo(importedBackgrounds));
            garden.SetVisible(true);
            battle.SetVisible(true);
            var gardenVeil = RendererNamed(garden.transform, "Dark center veil");
            var battleVeil = RendererNamed(battle.transform, "Dark dueling veil");

            float[] aspects = { 16f / 9f, 1f, 9f / 16f };
            for (int i = 0; i < aspects.Length; i++)
            {
                camera.aspect = aspects[i];
                camera.orthographicSize = 4.5f + i * .5f;
                camera.transform.position = new Vector3(6 + i * 2.3f, 2 - i * 1.1f, -10);
                garden.RefreshLayout();
                battle.RefreshLayout();
                AssertCoversViewport(garden.Background.GetComponent<SpriteRenderer>());
                AssertCoversViewport(battle.Background.GetComponent<SpriteRenderer>());
                AssertCoversViewport(gardenVeil);
                AssertCoversViewport(battleVeil);
            }
            AssertOwnedScene(garden.transform);
            AssertOwnedScene(battle.transform);
        }

        [TestCase(16f)]
        [TestCase(400f)]
        public void ImportedSpritesKeepTheirCropAspectCenterAndGameplaySize(float pixelsPerUnit)
        {
            var config = Configuration(true, pixelsPerUnit);
            var court = CreateCourt(config);
            var game = court.Game;
            Assert.That(game.UsesConfiguredBallSprite, Is.True);
            Assert.That(court.Ball.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(court.Ball.GetComponent<CircleCollider2D>().radius, Is.EqualTo(.22f).Within(.0001f));
            Assert.That(game.BallLightRadius, Is.EqualTo(1.8f).Within(.0001f));

            AssertArtwork(config.ball, court.Ball.GetComponent<SpriteRenderer>(), .44f, .44f);
            var target = game.CollectionTargetRenderer(4);
            AssertArtwork(config.bud, target, .38f, .38f);

            // Inspect each real configured icon renderer, including both pickup
            // slots and the attached ball glyph. Activating artwork does not alter
            // item state; it makes the displayed bounds available in EditMode.
            int iconCount = 0;
            foreach (var view in game.GetComponentsInChildren<SpriteRenderer>(true))
            {
                Sprite source = IconSource(config, view.sprite);
                if (source == null) continue;
                ShowForGeometry(view);
                AssertArtwork(source, view, .52f, .52f);
                iconCount++;
            }
            Assert.That(iconCount, Is.GreaterThanOrEqualTo(3),
                "All three configured spell symbols must reach actual renderers.");

            var garden = game.GardenPresentation;
            garden.SetVisible(true);
            garden.StepPresentation(1.1f, false);
            var bud = garden.BudRenderer(0);
            AssertArtwork(config.bud, bud, .38f, .38f);
            Vector2 closedSize = SizeAlongSpriteAxes(bud);
            garden.NotifyBudBloomed(0);
            garden.StepPresentation(.7f, false);
            Assert.That(garden.IsAwake(0), Is.True);
            var flower = garden.FlowerRenderer(0);
            Assert.That(flower.enabled, Is.True);
            AssertArtwork(config.flower, flower, .60f, .66f);
            Vector2 openSize = SizeAlongSpriteAxes(flower);

            // A second import of identical pixels with another PPU must be visually
            // indistinguishable in size, including the animated bud/flower layers.
            var alternateConfig = Configuration(true, pixelsPerUnit * 7);
            var alternate = CreateCourt(alternateConfig);
            var alternateGarden = alternate.Game.GardenPresentation;
            alternateGarden.StepPresentation(1.1f, false);
            var alternateBud = alternateGarden.BudRenderer(0);
            AssertSizeEqual(closedSize, SizeAlongSpriteAxes(alternateBud), "Closed bud size must ignore import PPU.");
            alternateGarden.NotifyBudBloomed(0);
            alternateGarden.StepPresentation(.7f, false);
            AssertSizeEqual(openSize, SizeAlongSpriteAxes(alternateGarden.FlowerRenderer(0)),
                "Bloomed flower size must ignore import PPU.");
            AssertOwnedScene(game.transform);
            AssertOwnedScene(alternate.Game.transform);
        }

        [Test]
        public void BrightImportedBackgroundDoesNotRevealClosedBudsOrHalosOutsideTheBallRadius()
        {
            var config = Configuration(true, 41);
            var court = CreateCourt(config);
            var game = court.Game;
            game.SetPaused(true);
            game.GardenPresentation.StepPresentation(1.1f, false);
            Assert.That(game.GardenPresentation.UsesConfiguredBackground, Is.True);
            var bud = game.CollectionTargetRenderer(4);
            var halo = game.CollectionHaloRenderer(4);
            Vector2 target = game.CollectionTargetPosition(4);

            foreach (float radius in new[] { .5f, 1.8f, 4f })
            {
                game.BallLightRadius = radius;
                MoveBall(court.Ball, target + Vector2.up * (radius + .02f));
                Invoke(game, "RefreshLighting");
                Assert.That(game.CoinVisibility(4), Is.Zero);
                Assert.That(IsVisible(bud), Is.False, "A bright backdrop cannot reveal a distant closed bud.");
                Assert.That(IsVisible(halo), Is.False, "A distant bud's halo must also remain absent.");

                MoveBall(court.Ball, target + Vector2.up * (radius * .70f));
                Invoke(game, "RefreshLighting");
                Assert.That(IsVisible(bud), Is.True, "The same imported bud must return inside the real light radius.");
                Assert.That(IsVisible(halo), Is.True);
            }
            Assert.That(court.Ball.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(court.Ball.GetComponent<CircleCollider2D>().radius, Is.EqualTo(.22f).Within(.0001f));
        }

        [Test]
        public void OffsetCourtKeepsBloomsOnContactCentersAndRetractsEveryVineAboveTheViewport()
        {
            var court = CreateCourt(Configuration(false, 64));
            var garden = court.Game.GardenPresentation;
            Assert.That(camera.transform.position.x, Is.Not.EqualTo(0),
                "This regression requires a non-zero court offset.");
            garden.StepPresentation(1.1f, false);
            Assert.That(garden.TargetsReady, Is.True);
            Assert.That(garden.GetComponentsInChildren<Collider2D>(true), Is.Empty);
            for (int index = 0; index < RallyGardenWorld.BudCount; index++)
            {
                Vector2 target = court.Game.CollectionTargetPosition(index);
                Assert.That(Vector2.Distance(garden.TargetWorldPosition(index), target), Is.LessThan(.001f));
                Assert.That(Vector2.Distance(garden.BudRenderer(index).bounds.center, target), Is.LessThan(.001f));
                garden.NotifyBudBloomed(index);
                garden.NotifyBudBloomed(index);
                Assert.That(garden.IsAwake(index), Is.True);
            }
            garden.StepPresentation(.7f, false);
            Assert.That(garden.AwakenedBudCount, Is.EqualTo(RallyGardenWorld.BudCount), "Repeated notification must not double-count flowers.");
            for (int index = 0; index < RallyGardenWorld.BudCount; index++)
                Assert.That(Vector2.Distance(garden.FlowerRenderer(index).bounds.center,
                    court.Game.CollectionTargetPosition(index)), Is.LessThan(.001f));
            garden.StepPresentation(2.2f, true);
            Assert.That(garden.RetractionProgress, Is.EqualTo(1).Within(.001f));
            Assert.That(garden.TargetsReady, Is.False);
            for (int index = 0; index < RallyGardenWorld.BudCount; index++)
            foreach (var renderer in garden.FlowerRenderer(index).transform.parent.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled)
                    Assert.That(renderer.bounds.min.y,
                        Is.GreaterThan(camera.transform.position.y + camera.orthographicSize),
                        "Every flower layer, including its glow, must leave through the top before replacement: " + renderer.name);
            AssertOwnedScene(garden.transform);
        }

        [Test]
        public void ChangingNetHeightKeepsItsDisplayedTopBottomAndWidthOnThePhysicalCollider()
        {
            var court = CreateCourt(Configuration(false, 64));
            court.Game.SetMode(RallyMode.Versus);
            var world = court.Game.CyberPresentation;
            var line = RendererNamed(world.transform, "Fine center net");
            foreach (float height in new[] { 1f, 2f, 3.1f, 1f })
            {
                court.Game.NetHeight = height;
                Physics2D.SyncTransforms();
                Bounds collision = court.Game.NetCollider.bounds;
                Assert.That(line.enabled, Is.True, "The minimum physical net still has a visible vertical span.");
                Bounds displayed = line.bounds;
                Assert.That(displayed.min.y, Is.EqualTo(collision.min.y).Within(.015f),
                    "Adjusting the net must not shift its displayed bottom up to the scoring floor.");
                Assert.That(displayed.max.y, Is.EqualTo(collision.max.y).Within(.015f));
                Assert.That(displayed.center.x, Is.EqualTo(collision.center.x).Within(.001f));
                Assert.That(displayed.size.x, Is.GreaterThan(.005f).And.LessThanOrEqualTo(collision.size.x + .001f),
                    "The decorative center line must stay narrow enough to represent the physical net.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DestroyingComponentsReleasesGeneratedArtworkAndRetainsSharedImportedAssets(bool imported)
        {
            var config = Configuration(imported, 73);
            var court = CreateCourt(config);
            var garden = court.Game.GardenPresentation;
            var battle = court.Game.CyberPresentation;
            var gardenOwner = garden.gameObject;
            var battleOwner = battle.gameObject;
            var gardenOwned = OwnedWorldResources(garden);
            var battleOwned = OwnedWorldResources(battle);
            Assert.That(gardenOwned.Count, Is.GreaterThan(4));
            Assert.That(battleOwned.Count, Is.GreaterThan(4));

            Object.DestroyImmediate(garden);
            AssertDestroyed(gardenOwned, "Garden component destruction");
            Assert.That(gardenOwner.GetComponentsInChildren<Renderer>(true), Is.Empty,
                "Destroying only the garden component must also remove its generated scene objects.");
            AssertImportedAssetsAlive(config, imported);
            Object.DestroyImmediate(battle);
            AssertDestroyed(battleOwned, "Battle component destruction");
            Assert.That(battleOwner.GetComponentsInChildren<Renderer>(true), Is.Empty);
            AssertImportedAssetsAlive(config, imported);

            var ballView = court.Ball.GetComponent<SpriteRenderer>();
            Object.DestroyImmediate(court.Game);
            Assert.That(court.Owner.transform.childCount, Is.Zero,
                "Destroying only the game component must release its court objects.");
            Assert.That(ballView.sprite, Is.SameAs(court.OriginalBall),
                "The pre-existing ball sprite must be restored, not destroyed with a wrapper.");
            // This snapshot includes unreferenced newly-created sprites, so it also
            // catches a procedural fallback lost when an imported sprite replaces it.
            AssertDestroyed(court.Generated, "Game component destruction");
            AssertImportedAssetsAlive(config, imported);
            Assert.That(court.OriginalBall != null, Is.True);
            Assert.That(court.OriginalBall.texture != null, Is.True);
        }

        Court CreateCourt(RallyVisualConfig config)
        {
            var ballObject = Create("Existing shared ball");
            var original = ImportedSprite("Original ball", new Rect(3, 5, 32, 32), new Vector2(.5f, .5f), 64);
            var ball = ballObject.AddComponent<AirHockeyBall>();
            // Runtime Awake is not automatically delivered to an ordinary component
            // in EditMode. Invoke its normal initialization once, without replacing
            // fields or inventing a different rigidbody setup for the tests.
            if (ball.Body == null) Invoke(ball, "Awake");
            Own(ball.GetComponent<CircleCollider2D>().sharedMaterial);
            ball.GetComponent<CircleCollider2D>().radius = .22f;
            ball.GetComponent<SpriteRenderer>().sprite = original;
            ball.GetComponent<SpriteRenderer>().sortingOrder = 20;
            var owner = Create("Visual test court");
            var game = owner.AddComponent<CooperativeCoinGame>();
            HashSet<Object> before = ExistingArtResources();
            game.Configure(ball, camera, null, config);
            var generated = CaptureNewArtResources(before);
            return new Court { Owner = owner, Game = game, Ball = ball, OriginalBall = original, Generated = generated };
        }

        RallyVisualConfig Configuration(bool imported, float pixelsPerUnit)
        {
            var config = Own(ScriptableObject.CreateInstance<RallyVisualConfig>());
            if (!imported) return config;
            config.collectionBackground = ImportedSprite("Bright glasshouse background", new Rect(7, 11, 192, 108),
                new Vector2(.12f, .85f), pixelsPerUnit);
            config.battleBackground = ImportedSprite("Bright dueling background", new Rect(21, 9, 160, 100),
                new Vector2(.90f, .20f), pixelsPerUnit);
            config.ball = ImportedSprite("Imported ball", new Rect(31, 23, 96, 48), new Vector2(.08f, .83f), pixelsPerUnit);
            config.star = ImportedSprite("Imported star", new Rect(11, 19, 48, 96), new Vector2(.93f, .12f), pixelsPerUnit);
            config.bud = ImportedSprite("Imported bud", new Rect(17, 9, 40, 100), new Vector2(.85f, .10f), pixelsPerUnit);
            config.flower = ImportedSprite("Imported flower", new Rect(13, 21, 100, 60), new Vector2(.10f, .90f), pixelsPerUnit);
            config.fireballIcon = ImportedSprite("Imported fireball", new Rect(9, 17, 96, 48), new Vector2(.18f, .92f), pixelsPerUnit);
            config.timerIcon = ImportedSprite("Imported timer", new Rect(17, 13, 48, 96), new Vector2(.88f, .15f), pixelsPerUnit);
            config.lightIcon = ImportedSprite("Imported lantern", new Rect(15, 7, 80, 60), new Vector2(.08f, .15f), pixelsPerUnit);
            return config;
        }

        Sprite ImportedSprite(string name, Rect rect, Vector2 pivot, float pixelsPerUnit)
        {
            var texture = Own(new Texture2D(256, 256, TextureFormat.RGBA32, false));
            texture.name = name + " shared texture";
            var pixels = new Color32[texture.width * texture.height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Own(Sprite.Create(texture, rect, pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect));
            sprite.name = name + " shared sprite";
            return sprite;
        }

        void AssertCoversViewport(SpriteRenderer renderer)
        {
            Assert.That(renderer, Is.Not.Null);
            Bounds bounds = renderer.bounds;
            Vector3 center = camera.transform.position;
            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * camera.aspect;
            Assert.That(bounds.min.x, Is.LessThanOrEqualTo(center.x - halfWidth + .005f), renderer.name + " left edge");
            Assert.That(bounds.max.x, Is.GreaterThanOrEqualTo(center.x + halfWidth - .005f), renderer.name + " right edge");
            Assert.That(bounds.min.y, Is.LessThanOrEqualTo(center.y - halfHeight + .005f), renderer.name + " bottom edge");
            Assert.That(bounds.max.y, Is.GreaterThanOrEqualTo(center.y + halfHeight - .005f), renderer.name + " top edge");
            Assert.That(bounds.center.x, Is.EqualTo(center.x).Within(.01f), renderer.name + " center x");
            Assert.That(bounds.center.y, Is.EqualTo(center.y).Within(.01f), renderer.name + " center y");
        }

        static void AssertArtwork(Sprite source, SpriteRenderer renderer, float minimumSize, float maximumSize)
        {
            Assert.That(renderer.sprite, Is.Not.Null);
            Assert.That(renderer.sprite, Is.Not.SameAs(source), "The imported sprite must stay immutable.");
            Assert.That(renderer.sprite.texture, Is.SameAs(source.texture));
            Assert.That(renderer.sprite.rect, Is.EqualTo(source.rect), "The selected source crop must be retained.");
            Vector2 size = SizeAlongSpriteAxes(renderer);
            Assert.That(size.x / size.y, Is.EqualTo(source.rect.width / source.rect.height).Within(.01f),
                renderer.name + " must preserve the imported artwork's aspect ratio.");
            float largest = Mathf.Max(size.x, size.y);
            Assert.That(largest, Is.GreaterThanOrEqualTo(minimumSize - .001f).And.LessThanOrEqualTo(maximumSize + .001f),
                renderer.name + " has an unexpected world-space size.");
            Vector3 center = renderer.transform.TransformPoint(renderer.localBounds.center);
            Assert.That(Vector2.Distance(center, renderer.transform.position), Is.LessThan(.001f),
                renderer.name + " must be centered on its gameplay/ornament anchor despite an imported custom pivot.");
        }

        static Vector2 SizeAlongSpriteAxes(SpriteRenderer renderer)
        {
            // Renderer.localBounds includes SpriteRenderer.size when sliced, unlike
            // Sprite.bounds. This measures what is drawn even for a rotated flower.
            Vector3 bounds = renderer.localBounds.size;
            Vector3 scale = renderer.transform.lossyScale;
            return new Vector2(bounds.x * Mathf.Abs(scale.x), bounds.y * Mathf.Abs(scale.y));
        }

        static void AssertSizeEqual(Vector2 expected, Vector2 actual, string message)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(.001f), message);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(.001f), message);
        }

        static Sprite IconSource(RallyVisualConfig config, Sprite wrapper)
        {
            if (wrapper == null) return null;
            foreach (Sprite source in new[] { config.fireballIcon, config.timerIcon, config.lightIcon })
                if (source != null && wrapper.texture == source.texture) return source;
            return null;
        }

        void ShowForGeometry(SpriteRenderer renderer)
        {
            for (Transform node = renderer.transform; node != null && node != root.transform; node = node.parent)
                node.gameObject.SetActive(true);
            renderer.enabled = true;
        }

        void AssertOwnedScene(Transform owner)
        {
            foreach (Transform child in owner.GetComponentsInChildren<Transform>(true))
                Assert.That(child.gameObject.scene, Is.EqualTo(scene), child.name + " escaped the owner's additive scene.");
        }

        static SpriteRenderer RendererNamed(Transform parent, string name)
        {
            foreach (var renderer in parent.GetComponentsInChildren<SpriteRenderer>(true))
                if (renderer.name == name) return renderer;
            Assert.Fail("Expected renderer was not created: " + name);
            return null;
        }

        static bool IsVisible(SpriteRenderer renderer) =>
            renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.color.a > .001f;

        static void MoveBall(AirHockeyBall ball, Vector2 position)
        {
            ball.Body.position = position;
            ball.transform.position = position;
            ball.Body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
        }

        static List<Object> OwnedWorldResources(MonoBehaviour world)
        {
            var field = world.GetType().GetField("ownedResources", InstanceMembers);
            Assert.That(field, Is.Not.Null);
            return new List<Object>((List<Object>)field.GetValue(world));
        }

        static void AssertDestroyed(List<Object> resources, string reason)
        {
            foreach (var resource in resources)
                Assert.That(resource == null, Is.True,
                    reason + " left generated art alive: " + (resource != null ? resource.name : "destroyed"));
        }

        static void AssertImportedAssetsAlive(RallyVisualConfig config, bool imported)
        {
            Assert.That(config != null, Is.True, "The shared configuration must survive component destruction.");
            if (!imported) return;
            foreach (Sprite sprite in new[] { config.collectionBackground, config.battleBackground, config.ball,
                config.star, config.bud, config.flower, config.fireballIcon, config.timerIcon, config.lightIcon })
            {
                Assert.That(sprite != null, Is.True, "A shared imported sprite was destroyed.");
                Assert.That(sprite.texture != null, Is.True, "A shared imported texture was destroyed.");
                Assert.That(sprite.texture.width, Is.EqualTo(256));
            }
        }

        static HashSet<Object> ExistingArtResources()
        {
            var result = new HashSet<Object>();
            foreach (var texture in Resources.FindObjectsOfTypeAll<Texture2D>()) result.Add(texture);
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>()) result.Add(sprite);
            return result;
        }

        List<Object> CaptureNewArtResources(HashSet<Object> before)
        {
            var result = new List<Object>();
            foreach (Object resource in ExistingArtResources())
            {
                if (before.Contains(resource) || EditorUtility.IsPersistent(resource)) continue;
                result.Add(resource);
                generatedAssets.Add(resource);
            }
            Assert.That(result.Count, Is.GreaterThan(10), "The fixture must observe real generated art resources.");
            return result;
        }

        static void Invoke(object target, string name, params object[] arguments)
        {
            var method = target.GetType().GetMethod(name, InstanceMembers);
            Assert.That(method, Is.Not.Null, "Missing production entrypoint: " + name);
            method.Invoke(target, arguments);
        }

        GameObject Create(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetParent(root.transform, false);
            return go;
        }

        T Own<T>(T value) where T : Object
        {
            if (value != null && !fixtureAssets.Contains(value)) fixtureAssets.Add(value);
            return value;
        }
    }
}
