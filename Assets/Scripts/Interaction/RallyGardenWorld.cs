using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Naispo.Interaction
{
    /// <summary>
    /// Render-only midnight glasshouse. Nine peripheral life buds track the shared
    /// collection round; the court owns every physical boundary and the light radius.
    /// </summary>
    // Explicit previews may allocate generated art in edit mode. Update only retracks
    // the camera layout; presentation time still advances only from StepPresentation.
    [ExecuteAlways]
    public sealed class RallyGardenWorld : MonoBehaviour
    {
        public const int BudCount = 9;
        const float TransferSeconds = .45f;
        const float BloomSeconds = 1.6f;
        const float BudWorldSize = .26f;
        const float FlowerWorldSize = .46f;
        readonly List<Object> ownedResources = new List<Object>();
        readonly SpriteRenderer[] buds = new SpriteRenderer[BudCount];
        readonly SpriteRenderer[] flowers = new SpriteRenderer[BudCount];
        readonly SpriteRenderer[] halos = new SpriteRenderer[BudCount];
        readonly SpriteRenderer[] motes = new SpriteRenderer[BudCount];
        readonly SpriteRenderer[] candleGlows = new SpriteRenderer[6];
        readonly Vector3[] budOrigins = new Vector3[BudCount];
        readonly bool[] awakened = new bool[BudCount];
        readonly bool[] transferActive = new bool[BudCount];
        readonly float[] transferAge = new float[BudCount];
        readonly float[] wakeAge = new float[BudCount];
        readonly float[] bloomLevels = new float[BudCount];
        readonly Vector2[] transferOrigins = new Vector2[BudCount];
        RallyVisualConfig config;
        GameObject scenery;
        Camera gardenCamera;
        Transform backdrop, veil;
        Sprite backdropSprite, veilSprite, lineSprite, glowSprite, budSprite, flowerSprite, moteSprite;
        bool backdropConfigured, visible = true, destroyed;
        float viewportHeight, viewportAspect, presentationTime, bloomElapsed, bloomProgress;
        bool roundComplete;
        Vector3 cameraPosition;

        public int AwakenedBudCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < awakened.Length; i++) if (awakened[i]) count++;
                return count;
            }
        }

        public int TransferCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < transferActive.Length; i++) if (transferActive[i]) count++;
                return count;
            }
        }

        public float BloomProgress => bloomProgress;
        public bool IsInitialized => scenery != null;
        public bool UsesConfiguredBackground => backdropConfigured;
        public int OwnedResourceCount => ownedResources.Count;
        public Transform Background => backdrop;
        public Sprite BackgroundSprite => backdropSprite;
        public bool IsAwake(int index) => index >= 0 && index < BudCount && awakened[index];
        public float FlowerBloom(int index) => index >= 0 && index < BudCount ? bloomLevels[index] : 0;
        public Vector2 BudLocalPosition(int index) => index >= 0 && index < BudCount
            ? new Vector2(budOrigins[index].x, budOrigins[index].y) : default;

        public void Initialize(float halfWidth, float floorY, Camera camera, RallyVisualConfig visualConfig = null)
        {
            if (scenery != null || camera == null || !camera.orthographic) return;
            config = visualConfig;
            gardenCamera = camera;
            scenery = CreateChild("Light garden", transform);
            scenery.transform.position = new Vector3(camera.transform.position.x, 0, 0);

            lineSprite = CreateOwnedTextureSprite(NewSolidTexture(), "Glasshouse line");
            glowSprite = CreateOwnedTextureSprite(CreateGlowTexture(64), "Glasshouse glow");
            budSprite = Normalized(config != null ? config.bud : null, BudWorldSize, "Configured life bud") ??
                Normalized(CreateOwnedTextureSprite(CreateBudTexture(), "Procedural life bud"), BudWorldSize,
                    "Procedural life bud");
            flowerSprite = Normalized(config != null ? config.flower : null, FlowerWorldSize, "Configured life flower") ??
                Normalized(CreateOwnedTextureSprite(CreateFlowerTexture(), "Procedural life flower"), FlowerWorldSize,
                    "Procedural life flower");
            moteSprite = CreateOwnedTextureSprite(CreateGlowTexture(32), "Life mote");

            BuildBackdrop(camera);
            // A supplied painting already contains the architecture and foliage.
            // Keep the living buds separate so collection still animates the room.
            if (!backdropConfigured) BuildArchitecture(halfWidth, floorY, camera);
            BuildBuds(halfWidth, floorY, camera);
            ApplyVisuals();
            RefreshLayout();
            scenery.SetActive(visible);
        }

        public void SetVisible(bool value)
        {
            visible = value;
            if (scenery != null) scenery.SetActive(value);
            if (value) { RefreshLayout(); ApplyVisuals(); }
        }

        /// <summary>Starts exactly one transfer for a successfully collected star.</summary>
        public void NotifyStarCollected(int index, Vector2 origin)
        {
            if (index < 0 || index >= BudCount || awakened[index] || transferActive[index]) return;
            transferActive[index] = true;
            transferAge[index] = 0;
            transferOrigins[index] = origin;
            if (motes[index] != null)
            {
                motes[index].enabled = true;
                motes[index].transform.position = origin;
            }
        }

        /// <summary>Clears round progress but keeps the generated scenery and layout.</summary>
        public void ResetRoundPresentation()
        {
            for (int i = 0; i < BudCount; i++)
            {
                awakened[i] = false; transferActive[i] = false;
                transferAge[i] = 0; wakeAge[i] = 0; bloomLevels[i] = 0;
                if (motes[i] != null) motes[i].enabled = false;
            }
            bloomElapsed = 0; bloomProgress = 0; roundComplete = false;
            ApplyVisuals();
        }

        /// <summary>
        /// Advances the only presentation clock. Callers pass zero while paused,
        /// waiting for input/settings/overlays, or when the game component is disabled.
        /// </summary>
        public void StepPresentation(float dt, bool roundCompleteNow)
        {
            float step = dt > 0 && !float.IsInfinity(dt) && !float.IsNaN(dt) ? dt : 0;
            presentationTime += step;
            roundComplete = roundCompleteNow;
            for (int i = 0; i < BudCount; i++)
            {
                if (transferActive[i])
                {
                    transferAge[i] += step;
                    if (transferAge[i] >= TransferSeconds)
                    {
                        transferActive[i] = false;
                        transferAge[i] = TransferSeconds;
                        if (!awakened[i]) { awakened[i] = true; wakeAge[i] = 0; }
                    }
                }
                if (awakened[i]) wakeAge[i] += step;
            }
            if (roundCompleteNow)
            {
                bloomElapsed = Mathf.Min(BloomSeconds, bloomElapsed + step);
                bloomProgress = Mathf.Clamp01(bloomElapsed / BloomSeconds);
            }
            else
            {
                bloomElapsed = 0; bloomProgress = 0;
            }
            ApplyVisuals();
        }

        public void RefreshLayout()
        {
            if (gardenCamera == null || scenery == null) return;
            viewportHeight = gardenCamera.orthographicSize;
            viewportAspect = gardenCamera.aspect;
            cameraPosition = gardenCamera.transform.position;
            float width = viewportHeight * viewportAspect * 2, height = viewportHeight * 2;
            Vector3 viewportCenter = new Vector3(cameraPosition.x, cameraPosition.y, 0);
            if (backdrop != null)
            {
                // Imported backgrounds cover preserving aspect; generated fallback art
                // (e.g. 512x288 authored with PPU=width) stretches to the whole viewport.
                RallySceneryArtwork.FitSpriteToViewport(backdrop, backdropSprite, viewportCenter,
                    width, height, backdropConfigured, config != null ? config.backgroundVerticalAlignment : .5f);
            }
            if (veil != null)
            {
                RallySceneryArtwork.FitSpriteToViewport(veil, veilSprite, viewportCenter, width, height, false);
            }
        }

        void Update()
        {
            if (!visible || scenery == null || gardenCamera == null) return;
            // Layout may follow a resized or moved camera; the court decoration does not.
            if (!Mathf.Approximately(viewportHeight, gardenCamera.orthographicSize) ||
                !Mathf.Approximately(viewportAspect, gardenCamera.aspect) ||
                cameraPosition != gardenCamera.transform.position) RefreshLayout();
        }

        void BuildBackdrop(Camera camera)
        {
            Sprite configured = Normalized(config != null ? config.collectionBackground : null, 1f, "Configured glasshouse background");
            backdropConfigured = configured != null;
            backdropSprite = configured ?? CreateOwnedTextureSprite(CreateBackgroundTexture(), "Midnight glasshouse background");
            var background = RallySceneryArtwork.AddRenderer(scenery.transform, "Midnight glasshouse background",
                backdropSprite, Color.white, -90, simple: true);
            if (backdropConfigured)
            {
                var material = Own(RallySceneryArtwork.CreateBackdropMaterial(backdropSprite, config,
                    out var blurredTexture, config.collectionBackgroundDepth));
                Own(blurredTexture);
                if (material != null) background.sharedMaterial = material;
            }
            backdrop = background.transform;

            veilSprite = CreateOwnedTextureSprite(CreateVeilTexture(), "Glasshouse center veil");
            var veilRenderer = RallySceneryArtwork.AddRenderer(scenery.transform, "Dark center veil",
                veilSprite, Color.white, -89, simple: true);
            veil = veilRenderer.transform;
            RefreshLayout();
        }

        void BuildArchitecture(float halfWidth, float floorY, Camera camera)
        {
            float top = camera.transform.position.y + camera.orthographicSize * .98f;
            float bottom = floorY - .85f;
            float height = Mathf.Max(2, top - bottom);
            Color glass = config != null ? config.gardenGlass : new Color(.10f, .34f, .38f);
            Color gold = config != null ? config.gardenGold : new Color(.78f, .64f, .34f);
            Color foliage = config != null ? config.gardenFoliage : new Color(.13f, .31f, .24f);
            Color candle = config != null ? config.gardenCandle : new Color(1f, .79f, .43f);
            Color dimGold = new Color(gold.r, gold.g, gold.b, .34f);
            Color dimGlass = new Color(glass.r, glass.g, glass.b, .11f);

            // Old library shelves remain outside the playable center.
            for (int side = -1; side <= 1; side += 2)
            {
                float edge = side * (halfWidth - .08f);
                RallySceneryArtwork.AddQuad(scenery.transform, "Library recess " + side, lineSprite,
                    new Color(.06f, .045f, .03f, .52f), -62, new Vector2(edge, floorY + height * .53f),
                    new Vector2(.44f, height * .88f));
                for (int shelf = 0; shelf < 4; shelf++)
                    RallySceneryArtwork.AddQuad(scenery.transform, "Library shelf " + side + " " + shelf,
                        lineSprite, new Color(gold.r, gold.g, gold.b, .16f), -61,
                        new Vector2(edge, floorY + height * (.22f + shelf * .18f)), new Vector2(.40f, .022f));
            }

            // Pointed glass arches, columns and restrained bronze ribs frame the sides.
            for (int side = -1; side <= 1; side += 2)
            {
                float outer = side * (halfWidth - .46f);
                float inner = side * halfWidth * .34f;
                float peak = floorY + height * .84f;
                AddPointedArch("Glass arch " + side, outer, inner, peak, floorY + height * .16f, dimGold, 11);
                AddPointedArch("Inner arch " + side, outer + side * .12f, inner - side * .12f,
                    peak - height * .05f, floorY + height * .28f, new Color(gold.r, gold.g, gold.b, .16f), 9);
                RallySceneryArtwork.AddQuad(scenery.transform, "Glass pane " + side, lineSprite, dimGlass,
                    -72, new Vector2((outer + inner) * .5f, floorY + height * .42f),
                    new Vector2(Mathf.Abs(inner - outer) * .82f, height * .48f));
                for (int division = 1; division < 3; division++)
                {
                    float x = Mathf.Lerp(outer, inner, division / 3f);
                    RallySceneryArtwork.AddQuad(scenery.transform, "Glass division " + side + " " + division,
                        lineSprite, new Color(glass.r, glass.g, glass.b, .24f), -60,
                        new Vector2(x, floorY + height * .34f), new Vector2(.016f, height * .30f));
                }
                RallySceneryArtwork.AddColumn(scenery.transform, "Outer column " + side, lineSprite,
                    new Color(glass.r * .55f, glass.g * .55f, glass.b * .55f, .38f), dimGold, -58,
                    outer, floorY - .05f, top, .12f);
                RallySceneryArtwork.AddColumn(scenery.transform, "Inner column " + side, lineSprite,
                    new Color(glass.r * .45f, glass.g * .45f, glass.b * .45f, .30f), dimGold, -58,
                    inner, floorY - .05f, top * .92f, .075f);
            }

            // Floor stays quiet; only one antique gold line and a faint mist edge.
            RallySceneryArtwork.AddQuad(scenery.transform, "Glasshouse floor", lineSprite,
                new Color(.018f, .062f, .064f, .42f), -66, new Vector2(0, floorY - .34f),
                new Vector2(halfWidth * 2, .68f));
            RallySceneryArtwork.AddQuad(scenery.transform, "Floor gold line", lineSprite,
                new Color(gold.r, gold.g, gold.b, .22f), -55, new Vector2(0, floorY + .005f),
                new Vector2(halfWidth * 1.9f, .018f));

            // Candle lanterns are decoration: no Light component, no gameplay glow.
            int candleIndex = 0;
            for (int side = -1; side <= 1; side += 2)
            for (int level = 0; level < 3; level++)
            {
                float x = side * (halfWidth - .58f);
                float y = floorY + height * (.28f + level * .19f);
                RallySceneryArtwork.AddQuad(scenery.transform, "Lantern stem " + candleIndex, lineSprite,
                    new Color(gold.r, gold.g, gold.b, .28f), -54, new Vector2(x, y - .10f),
                    new Vector2(.018f, .20f));
                RallySceneryArtwork.AddQuad(scenery.transform, "Lantern cage " + candleIndex, lineSprite,
                    new Color(gold.r, gold.g, gold.b, .34f), -53, new Vector2(x, y + .04f),
                    new Vector2(.10f, .14f));
                var glow = RallySceneryArtwork.AddRenderer(scenery.transform, "Lantern candle " + candleIndex,
                    glowSprite, new Color(candle.r, candle.g, candle.b, .10f), -52);
                glow.transform.localPosition = new Vector3(x, y + .04f, 0);
                glow.transform.localScale = new Vector3(.24f, .24f, 1);
                candleGlows[candleIndex++] = glow;
            }

            // Foliage is concentrated at the two lower edges.
            var leaf = CreateOwnedTextureSprite(CreateLeafTexture(), "Glasshouse leaf");
            int leafIndex = 0;
            for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 7; i++)
            {
                float x = side * (halfWidth - .72f - (i % 2) * .16f - (i / 5f) * .32f);
                float y = floorY + .10f + (i % 3) * .16f;
                var view = RallySceneryArtwork.AddQuad(scenery.transform, "Foliage " + leafIndex++, leaf,
                    new Color(foliage.r, foliage.g, foliage.b, .46f), -50, new Vector2(x, y),
                    new Vector2(.16f, .30f), side * (18 + i * 9));
                view.sortingOrder = -50 + i % 2;
            }
        }

        void AddPointedArch(string prefix, float left, float right, float peakY, float baseY, Color color, int segments)
        {
            float peakX = (left + right) * .5f;
            Vector2 previousLeft = new Vector2(left, baseY), previousRight = new Vector2(right, baseY);
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                float xLeft = Mathf.Lerp(left, peakX, t);
                float yLeft = Mathf.Lerp(baseY, peakY, Mathf.Pow(t, .72f));
                float xRight = Mathf.Lerp(right, peakX, t);
                float yRight = Mathf.Lerp(baseY, peakY, Mathf.Pow(t, .72f));
                RallySceneryArtwork.AddStroke(scenery.transform, prefix + " L" + i, lineSprite, color, -59,
                    previousLeft, new Vector2(xLeft, yLeft), .026f);
                RallySceneryArtwork.AddStroke(scenery.transform, prefix + " R" + i, lineSprite, color, -59,
                    previousRight, new Vector2(xRight, yRight), .026f);
                previousLeft = new Vector2(xLeft, yLeft);
                previousRight = new Vector2(xRight, yRight);
            }
        }

        void BuildBuds(float halfWidth, float floorY, Camera camera)
        {
            float height = camera.orthographicSize * 2;
            Color jade = config != null ? config.playerJade : new Color(.64f, .94f, .80f);
            Color gold = config != null ? config.gardenGold : new Color(.78f, .64f, .34f);
            for (int i = 0; i < BudCount; i++)
            {
                bool leftSide = i < 4;
                int local = leftSide ? i : i - 4;
                int count = leftSide ? 4 : 5;
                float x = (leftSide ? -1 : 1) * (halfWidth * .86f - (i % 2) * .10f);
                float y = floorY + height * (.10f + local * (.66f / Mathf.Max(1, count - 1)));
                budOrigins[i] = new Vector3(x, y, 0);
                var root = CreateChild("Life bud " + (i + 1), scenery.transform);
                root.transform.localPosition = budOrigins[i];
                var bud = RallySceneryArtwork.AddRenderer(root.transform, "Closed bud " + (i + 1),
                    budSprite, Muted(Color.Lerp(jade, gold, i / 8f * .55f), .58f), 5, simple: true);
                bud.transform.localPosition = Vector3.zero;
                bud.transform.localScale = Vector3.one;
                bud.transform.localRotation = Quaternion.Euler(0, 0, (i % 2 == 0 ? -1 : 1) * 7f);
                var halo = RallySceneryArtwork.AddRenderer(root.transform, "Bud halo " + (i + 1),
                    glowSprite, new Color(gold.r, gold.g, gold.b, .035f), 4, simple: true);
                halo.transform.localScale = new Vector3(.30f, .30f, 1);
                var flower = RallySceneryArtwork.AddRenderer(root.transform, "Flower " + (i + 1),
                    flowerSprite, new Color(1, 1, 1, 0), 6, simple: true);
                flower.transform.localPosition = new Vector3(0, -.01f, 0);
                flower.transform.localScale = Vector3.one * (BudWorldSize / FlowerWorldSize);
                flower.enabled = false;
                var stem = RallySceneryArtwork.AddQuad(root.transform, "Stem " + (i + 1), lineSprite,
                    new Color(jade.r * .5f, jade.g * .55f, jade.b * .5f, .28f), 3,
                    new Vector2(0, -.20f), new Vector2(.014f, .26f));
                stem.transform.localRotation = Quaternion.Euler(0, 0, (i % 2 == 0 ? 1 : -1) * 5f);
                buds[i] = bud; halos[i] = halo; flowers[i] = flower;
                var mote = RallySceneryArtwork.AddRenderer(scenery.transform, "Magic mote " + (i + 1),
                    moteSprite, new Color(gold.r, gold.g, gold.b, .9f), 8, simple: true);
                mote.transform.localScale = Vector3.one * .12f;
                mote.enabled = false;
                motes[i] = mote;
            }
        }

        void ApplyVisuals()
        {
            Color gold = config != null ? config.gardenGold : new Color(.78f, .64f, .34f);
            Color jade = config != null ? config.playerJade : new Color(.64f, .94f, .80f);
            float flicker = .86f + .14f * Mathf.Sin(presentationTime * 1.7f);
            for (int i = 0; i < candleGlows.Length; i++)
            {
                if (candleGlows[i] == null) continue;
                float phase = .84f + .16f * Mathf.Sin(presentationTime * 1.9f + i * .7f);
                candleGlows[i].color = new Color(1f, .78f, .43f, .085f * flicker * phase);
            }
            for (int i = 0; i < BudCount; i++)
            {
                if (buds[i] == null) continue;
                float wake = awakened[i] ? Mathf.Clamp01(wakeAge[i] / .28f) : 0;
                // A collected star lights and grows its own bud. The full flower is a
                // separate completion bloom, so no bud opens early before all nine.
                float open = roundComplete ? bloomProgress : 0;
                bloomLevels[i] = open;
                Color budColor = Color.Lerp(Muted(Color.Lerp(jade, gold, i / 8f * .55f), .58f), gold, wake);
                budColor.a = Mathf.Lerp(.55f, .92f, wake);
                buds[i].color = budColor;
                buds[i].transform.localScale = Vector3.one * (1 + wake * .10f);
                if (halos[i] != null)
                {
                    halos[i].enabled = wake > .001f || open > .001f;
                    halos[i].color = new Color(gold.r, gold.g, gold.b, .035f + wake * .075f + open * .03f);
                    halos[i].transform.localScale = Vector3.one * (.30f + wake * .10f);
                }
                if (flowers[i] != null)
                {
                    flowers[i].enabled = open > .001f;
                    flowers[i].color = new Color(1, 1, 1, open * .94f);
                    flowers[i].transform.localScale = Vector3.one *
                        Mathf.Lerp(BudWorldSize / FlowerWorldSize, 1f, open);
                    flowers[i].transform.localRotation = Quaternion.Euler(0, 0, presentationTime * 3.2f + i * 17f);
                }
                if (motes[i] == null) continue;
                if (!transferActive[i]) { motes[i].enabled = false; continue; }
                float amount = Mathf.Clamp01(transferAge[i] / TransferSeconds);
                Vector2 origin = transferOrigins[i];
                // Bud origins are decoration-local. Convert them to world space so the
                // mote still reaches the bud when the camera (and scenery) shifts on x/y.
                Vector2 target = scenery.transform.TransformPoint(budOrigins[i]);
                Vector2 position = Vector2.Lerp(origin, target, Mathf.SmoothStep(0, 1, amount));
                position += new Vector2(0, Mathf.Sin(amount * Mathf.PI) * .16f);
                motes[i].enabled = true;
                motes[i].transform.position = new Vector3(position.x, position.y, 0);
                motes[i].color = new Color(gold.r, gold.g, gold.b, Mathf.Lerp(.95f, .25f, amount));
            }
        }

        static Color Muted(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        Texture2D CreateBackgroundTexture()
        {
            const int width = 512, height = 288;
            var texture = RallySceneryArtwork.NewTexture(width, height, "Midnight glasshouse atmosphere");
            var pixels = new Color32[width * height];
            Color deep = config != null ? config.gardenDeep : new Color(.012f, .043f, .055f);
            Color glass = config != null ? config.gardenGlass : new Color(.10f, .34f, .38f);
            Color gold = config != null ? config.gardenGold : new Color(.78f, .64f, .34f);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float nx = (x + .5f) / width * 2 - 1;
                float ny = (y + .5f) / height;
                Color color = Color.Lerp(deep * .55f, deep * 1.55f, Mathf.SmoothStep(0, 1, ny));
                float side = Mathf.Pow(Mathf.Abs(nx), 3.1f);
                color += new Color(glass.r, glass.g, glass.b, 0) * (side * .34f * (.55f + .45f * ny));
                float center = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .48f, Mathf.Abs(nx)));
                color *= 1 - center * .34f;
                float shelf = Mathf.SmoothStep(.78f, .94f, Mathf.Abs(nx));
                if (shelf > 0)
                {
                    float boards = Mathf.Repeat(ny * 15f + .2f, 1f);
                    float boardLine = 1 - Mathf.SmoothStep(0, .10f, Mathf.Abs(boards - .5f));
                    color += new Color(gold.r, gold.g, gold.b, 0) * (shelf * (.018f + boardLine * .045f));
                    float upright = 1 - Mathf.SmoothStep(0, .045f, Mathf.Abs(Mathf.Repeat((x + .5f) / width * 28f, 1f) - .5f));
                    color += new Color(.18f, .12f, .07f, 0) * (shelf * upright * .10f);
                }
                color.a = 1;
                pixels[y * width + x] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        Texture2D CreateVeilTexture()
        {
            const int width = 160, height = 90;
            var texture = RallySceneryArtwork.NewTexture(width, height, "Glasshouse center veil");
            var pixels = new Color32[width * height];
            Color dark = config != null ? config.gardenDeep : new Color(.012f, .043f, .055f);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float nx = Mathf.Abs((x + .5f) / width * 2 - 1);
                float ny = Mathf.Abs((y + .5f) / height * 2 - 1);
                float center = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.18f, .92f, Mathf.Max(nx * .82f, ny)));
                // The painted room already has a quiet center; preserve its detail
                // and edge lighting while keeping the ball and stars legible.
                float opacity = backdropConfigured ? .24f : .48f;
                pixels[y * width + x] = new Color(dark.r, dark.g, dark.b, center * opacity);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        Texture2D CreateBudTexture()
        {
            const int width = 48, height = 80;
            var texture = RallySceneryArtwork.NewTexture(width, height, "Procedural closed life bud");
            var pixels = new Color32[width * height];
            Color jade = config != null ? config.playerJade : new Color(.64f, .94f, .80f);
            Color gold = config != null ? config.gardenGold : new Color(.78f, .64f, .34f);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float v = (y + .5f) / height;
                float half = 17f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(v * .96f + .02f)) * (1 - .46f * v);
                float dx = Mathf.Abs(x + .5f - width * .5f);
                float edge = Mathf.Clamp01((half - dx) / 1.6f);
                float rim = Mathf.Clamp01((dx - half + 3.2f) / 3.2f);
                Color color = Color.Lerp(jade * .72f, gold, Mathf.SmoothStep(0, 1, rim * .85f));
                color.a = edge;
                pixels[y * width + x] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        Texture2D CreateFlowerTexture()
        {
            const int size = 96;
            var texture = RallySceneryArtwork.NewTexture(size, size, "Procedural life flower");
            var pixels = new Color32[size * size];
            Color jade = config != null ? config.playerJade : new Color(.64f, .94f, .80f);
            Color gold = config != null ? config.gardenGold : new Color(.78f, .64f, .34f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f) / size * 2 - 1, dy = (y + .5f) / size * 2 - 1;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx);
                float petalRadius = .26f + .30f * Mathf.Pow(Mathf.Max(0, Mathf.Cos(angle * 5)), .55f);
                float petal = Mathf.Clamp01((petalRadius - radius) / .035f);
                float center = Mathf.Clamp01((.22f - radius) / .035f);
                Color petalColor = Color.Lerp(jade, gold, Mathf.Clamp01(.30f + .45f * Mathf.Cos(angle * 5 + .4f)));
                Color color = center > .5f ? gold : petalColor;
                color.a = Mathf.Max(petal * .88f, center);
                pixels[y * size + x] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        Texture2D CreateLeafTexture()
        {
            const int width = 32, height = 64;
            var texture = RallySceneryArtwork.NewTexture(width, height, "Procedural glasshouse leaf");
            var pixels = new Color32[width * height];
            Color leaf = config != null ? config.gardenFoliage : new Color(.13f, .31f, .24f);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float v = (y + .5f) / height;
                float half = 15f * Mathf.Sin(Mathf.PI * v);
                float dx = Mathf.Abs(x + .5f - width * .5f);
                float alpha = Mathf.Clamp01((half - dx) / 1.4f);
                Color color = Color.Lerp(leaf * .55f, leaf * 1.2f, v);
                color.a = alpha * .82f;
                pixels[y * width + x] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        Texture2D CreateGlowTexture(int size)
        {
            var texture = RallySceneryArtwork.NewTexture(size, size, "Glasshouse soft round light");
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float radius = Vector2.Distance(new Vector2(x + .5f, y + .5f), Vector2.one * size * .5f) / (size * .5f);
                float alpha = Mathf.Pow(Mathf.Clamp01(1 - radius), 2.2f);
                pixels[y * size + x] = new Color(1, 1, 1, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        Texture2D NewSolidTexture()
        {
            var texture = RallySceneryArtwork.NewTexture(1, 1, "Glasshouse solid");
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, true);
            return texture;
        }

        Sprite CreateOwnedTextureSprite(Texture2D texture, string spriteName)
        {
            Own(texture);
            var sprite = RallySceneryArtwork.CreateSprite(texture, spriteName);
            Own(sprite);
            return sprite;
        }

        Sprite Normalized(Sprite source, float size, string wrapperName)
        {
            if (source == null) return null;
            var wrapper = RallySceneryArtwork.CreateNormalizedSprite(source, size, wrapperName);
            if (wrapper != null) Own(wrapper);
            return wrapper;
        }

        GameObject CreateChild(string name, Transform parent)
        {
            var child = new GameObject(name);
            SceneManager.MoveGameObjectToScene(child, gameObject.scene);
            child.transform.SetParent(parent, false);
            return child;
        }

        T Own<T>(T resource) where T : Object
        {
            if (resource != null) ownedResources.Add(resource);
            return resource;
        }

        void OnEnable()
        {
            // Re-enabling restores the same generated geometry; it never rebuilds it.
            if (scenery != null) scenery.SetActive(visible);
        }

        void OnDisable()
        {
            if (scenery != null) scenery.SetActive(false);
        }

        void OnDestroy()
        {
            if (destroyed) return;
            destroyed = true;
            Release(scenery);
            scenery = null;
            for (int i = ownedResources.Count - 1; i >= 0; i--) Release(ownedResources[i]);
            ownedResources.Clear();
        }

        static void Release(Object resource)
        {
            if (resource == null) return;
            if (resource is RenderTexture texture) texture.Release();
            if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
        }
    }
}
