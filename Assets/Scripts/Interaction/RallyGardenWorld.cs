using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Naispo.Interaction
{
    /// <summary>
    /// Render-only midnight glasshouse. Hanging light flowers occupy the shared
    /// collection targets; the court owns collisions and the ball reveal radius.
    /// </summary>
    // Explicit previews may allocate generated art in edit mode. Update only retracks
    // the camera layout; presentation time still advances only from StepPresentation.
    [ExecuteAlways]
    public sealed class RallyGardenWorld : MonoBehaviour
    {
        public const int BudCount = 9;
        public const float RoundTransitionSeconds = 2.2f;
        const float ArrivalSeconds = 1f;
        const float FlowerOpenSeconds = .42f;
        const float CompletionHoldSeconds = .85f;
        const float RetractSeconds = 1.15f;
        const float BudWorldSize = .38f;
        const float FlowerWorldSize = .60f;
        const int VineSegments = 20;
        const int LeavesPerVine = 4;
        readonly List<Object> ownedResources = new List<Object>();
        readonly Transform[] targets = new Transform[BudCount];
        readonly SpriteRenderer[] buds = new SpriteRenderer[BudCount];
        readonly SpriteRenderer[] flowers = new SpriteRenderer[BudCount];
        readonly SpriteRenderer[] flowerRadiance = new SpriteRenderer[BudCount];
        readonly SpriteRenderer[] flowerHearts = new SpriteRenderer[BudCount];
        readonly SpriteRenderer[] halos = new SpriteRenderer[BudCount];
        readonly SpriteRenderer[,] vines = new SpriteRenderer[BudCount, VineSegments];
        readonly SpriteRenderer[,] leaves = new SpriteRenderer[BudCount, LeavesPerVine];
        readonly SpriteRenderer[] candleGlows = new SpriteRenderer[6];
        readonly Vector3[] budOrigins = new Vector3[BudCount];
        readonly Vector2[] targetPositions = new Vector2[BudCount];
        readonly bool[] awakened = new bool[BudCount];
        readonly float[] wakeAge = new float[BudCount];
        readonly float[] bloomLevels = new float[BudCount];
        readonly float[] reveals = new float[BudCount];
        RallyVisualConfig config;
        GameObject scenery;
        Camera gardenCamera;
        Transform backdrop, veil;
        Sprite backdropSprite, veilSprite, lineSprite, glowSprite, budSprite, flowerSprite, vineLeafSprite;
        Sprite flowerRadianceSprite;
        Material flowerEmissionMaterial, flowerBloomMaterial;
        Material candleMaterial;
        static readonly int CandleTime = Shader.PropertyToID("_CandleTime");
        bool backdropConfigured, visible = true, destroyed;
        float viewportHeight, viewportAspect, presentationTime, bloomElapsed, bloomProgress;
        float arrivalElapsed = ArrivalSeconds;
        int roundIndex;
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

        // Compatibility for consumers of the former peripheral-bud presentation.
        public int TransferCount => 0;
        public float BloomProgress => bloomProgress;
        public float ArrivalProgress => Mathf.Clamp01(arrivalElapsed / ArrivalSeconds);
        public float RetractionProgress => Mathf.Clamp01((bloomElapsed - CompletionHoldSeconds) /
            (RoundTransitionSeconds - CompletionHoldSeconds));
        public bool TargetsReady => ArrivalProgress >= 1 && !roundComplete;
        public bool IsInitialized => scenery != null;
        public bool UsesConfiguredBackground => backdropConfigured;
        public int OwnedResourceCount => ownedResources.Count;
        public Transform Background => backdrop;
        public Sprite BackgroundSprite => backdropSprite;
        public bool IsAwake(int index) => Valid(index) && awakened[index];
        public float FlowerBloom(int index) => Valid(index) ? bloomLevels[index] : 0;
        public Vector2 BudLocalPosition(int index) => Valid(index)
            ? new Vector2(budOrigins[index].x, budOrigins[index].y) : default;
        public Vector2 TargetWorldPosition(int index) => Valid(index)
            ? targets[index] != null ? (Vector2)targets[index].position : targetPositions[index] : default;
        public SpriteRenderer BudRenderer(int index) => Valid(index) ? buds[index] : null;
        public SpriteRenderer FlowerRenderer(int index) => Valid(index) ? flowers[index] : null;
        public SpriteRenderer HaloRenderer(int index) => Valid(index) ? halos[index] : null;
        static bool Valid(int index) => index >= 0 && index < BudCount;

        public void Initialize(float halfWidth, float floorY, Camera camera, RallyVisualConfig visualConfig = null)
        {
            if (scenery != null || camera == null || !camera.orthographic) return;
            config = visualConfig;
            gardenCamera = camera;
            scenery = CreateChild("Light garden", transform);
            scenery.transform.position = new Vector3(camera.transform.position.x, 0, 0);

            lineSprite = CreateOwnedTextureSprite(NewSolidTexture(), "Glasshouse line");
            glowSprite = CreateOwnedTextureSprite(CreateGlowTexture(64), "Glasshouse glow");
            budSprite = Normalized(config != null ? config.bud : null, BudWorldSize, "Configured hanging bud") ??
                Normalized(CreateOwnedTextureSprite(RallyHangingFlowerArtwork.CreateBudTexture(), "Procedural hanging bud"), BudWorldSize,
                    "Procedural hanging bud");
            flowerSprite = Normalized(config != null ? config.flower : null, FlowerWorldSize, "Configured light flower") ??
                Normalized(CreateOwnedTextureSprite(RallyHangingFlowerArtwork.CreateFlowerTexture(), "Procedural light flower"), FlowerWorldSize,
                    "Procedural light flower");
            flowerEmissionMaterial = Own(RallyFlowerRadianceArtwork.CreateEmissionMaterial(1.45f));
            flowerBloomMaterial = Own(RallyFlowerRadianceArtwork.CreateBloomMaterial(1.8f));
            flowerRadianceSprite = Own(RallyFlowerRadianceArtwork.CreateBloomSprite(flowerSprite, out var flowerBloomTexture));
            Own(flowerBloomTexture);
            vineLeafSprite = CreateOwnedTextureSprite(CreateLeafTexture(), "Hanging vine leaf");

            BuildBackdrop(camera);
            // A supplied painting already contains the architecture and foliage.
            // Targets hang in front of the painting and react to the ball independently.
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

        /// <summary>Positions are court/world coordinates; decoration never moves the collision targets.</summary>
        public void SetTargetPositions(Vector2[] worldPositions, int round)
        {
            if (worldPositions == null) return;
            roundIndex = Mathf.Max(0, round);
            for (int i = 0; i < Mathf.Min(BudCount, worldPositions.Length); i++)
            {
                Vector2 position = worldPositions[i];
                if (!float.IsFinite(position.x) || !float.IsFinite(position.y)) continue;
                targetPositions[i] = position;
                budOrigins[i] = scenery != null ? scenery.transform.InverseTransformPoint(position) : (Vector3)position;
            }
            ApplyVisuals();
        }

        /// <summary>The caller supplies only the actual ball-light reveal amount.</summary>
        public void SetTargetReveal(int index, float reveal)
        {
            if (!Valid(index)) return;
            reveals[index] = float.IsNaN(reveal) ? 0 : Mathf.Clamp01(reveal);
            ApplyTargetVisual(index);
        }

        /// <summary>A contact opens this flower once; bloomed flowers remain visible.</summary>
        public void NotifyBudBloomed(int index)
        {
            if (!Valid(index) || awakened[index]) return;
            awakened[index] = true;
            wakeAge[index] = 0;
            ApplyTargetVisual(index);
        }

        public void NotifyStarCollected(int index, Vector2 origin) => NotifyBudBloomed(index);

        /// <summary>Fresh closed buds descend on their vines before the next serve.</summary>
        public void ResetRoundPresentation()
        {
            for (int i = 0; i < BudCount; i++)
            {
                awakened[i] = false;
                wakeAge[i] = 0;
                bloomLevels[i] = 0;
                reveals[i] = 0;
            }
            arrivalElapsed = 0;
            bloomElapsed = 0;
            bloomProgress = 0;
            roundComplete = false;
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
            arrivalElapsed = Mathf.Min(ArrivalSeconds, arrivalElapsed + step);
            roundComplete = roundCompleteNow;
            for (int i = 0; i < BudCount; i++)
                if (awakened[i]) wakeAge[i] = Mathf.Min(FlowerOpenSeconds + 1f, wakeAge[i] + step);
            if (roundCompleteNow)
            {
                bloomElapsed = Mathf.Min(RoundTransitionSeconds, bloomElapsed + step);
                bloomProgress = Mathf.Clamp01(bloomElapsed / CompletionHoldSeconds);
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
                if (material != null)
                {
                    background.sharedMaterial = material;
                    material.SetFloat("_AmbientExposure", Mathf.Clamp(config.gardenAmbientExposure, .05f, 1f));
                    material.SetFloat("_EmissionExposure", Mathf.Clamp(config.gardenCandleExposure, 1f, 2f));
                    if (config.collectionBackgroundDepth == RallyBackdropDepthProfile.Glasshouse)
                    {
                        var emission = Own(RallyCandleArtwork.CreateEmissionMask(backdropSprite));
                        if (emission != null) material.SetTexture("_EmissionMask", emission);
                    }
                    if (config.animateGlasshouseCandles &&
                        config.collectionBackgroundDepth == RallyBackdropDepthProfile.Glasshouse)
                    {
                        var mask = Own(RallyCandleArtwork.CreateMotionMask(backdropSprite));
                        if (mask != null)
                        {
                            material.SetTexture("_CandleMask", mask);
                            material.SetVector("_CandleMotionScale", RallyCandleArtwork.MotionScale(backdropSprite));
                            material.SetFloat("_CandleStrength", 1);
                            candleMaterial = material;
                        }
                    }
                }
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
            float halfHeight = camera.orthographicSize;
            Vector2 center = camera.transform.position;
            for (int i = 0; i < BudCount; i++)
            {
                // A useful standalone preview; the game immediately supplies its own arc.
                float u = i / (float)(BudCount - 1) * 2 - 1;
                targetPositions[i] = center + new Vector2(u * halfWidth * .49f,
                    Mathf.Lerp(-halfHeight * .06f, halfHeight * .52f, 1 - u * u));
                budOrigins[i] = scenery.transform.InverseTransformPoint(targetPositions[i]);
                var root = CreateChild("Hanging light flower " + (i + 1), scenery.transform);
                targets[i] = root.transform;
                root.transform.position = targetPositions[i];
                buds[i] = RallySceneryArtwork.AddRenderer(root.transform, "Closed bud " + (i + 1),
                    budSprite, Color.clear, 7, simple: true);
                flowers[i] = RallySceneryArtwork.AddRenderer(root.transform, "Flower " + (i + 1),
                    flowerSprite, Color.clear, 8, simple: true);
                if (flowerEmissionMaterial != null) flowers[i].sharedMaterial = flowerEmissionMaterial;
                flowerRadiance[i] = RallySceneryArtwork.AddRenderer(root.transform, "Flower radiance " + (i + 1),
                    flowerRadianceSprite, Color.clear, 6, simple: true);
                flowerHearts[i] = RallySceneryArtwork.AddRenderer(root.transform, "Flower heart " + (i + 1),
                    glowSprite, Color.clear, 9, simple: true);
                if (flowerBloomMaterial != null)
                {
                    flowerRadiance[i].sharedMaterial = flowerHearts[i].sharedMaterial = flowerBloomMaterial;
                }
                halos[i] = RallySceneryArtwork.AddRenderer(root.transform, "Bud halo " + (i + 1),
                    glowSprite, Color.clear, 6, simple: true);
                buds[i].enabled = flowers[i].enabled = halos[i].enabled = false;
                flowerRadiance[i].enabled = flowerHearts[i].enabled = false;
                for (int segment = 0; segment < VineSegments; segment++)
                    vines[i, segment] = RallySceneryArtwork.AddRenderer(scenery.transform,
                        "Vine " + (i + 1) + " segment " + segment, lineSprite, Color.clear, -8);
                for (int leaf = 0; leaf < LeavesPerVine; leaf++)
                    leaves[i, leaf] = RallySceneryArtwork.AddRenderer(scenery.transform,
                        "Vine " + (i + 1) + " leaf " + leaf, vineLeafSprite, Color.clear, -7, simple: true);
            }
        }

        void ApplyVisuals()
        {
            // This explicit, pause-aware clock also drives the candles in the painting.
            // Neither flowers nor their halos create scene lights or reveal other buds.
            if (candleMaterial != null) candleMaterial.SetFloat(CandleTime, presentationTime);
            float flicker = .86f + .14f * Mathf.Sin(presentationTime * 1.7f);
            for (int i = 0; i < candleGlows.Length; i++)
            {
                if (candleGlows[i] == null) continue;
                float phase = .84f + .16f * Mathf.Sin(presentationTime * 1.9f + i * .7f);
                candleGlows[i].color = new Color(1f, .78f, .43f, .085f * flicker * phase);
            }
            for (int i = 0; i < BudCount; i++) ApplyTargetVisual(i);
        }

        void ApplyTargetVisual(int index)
        {
            if (buds[index] == null || gardenCamera == null) return;
            Vector2 destination = targetPositions[index];
            float top = gardenCamera.transform.position.y + gardenCamera.orthographicSize + .34f;
            float parkedY = top + .68f;
            float descend = Mathf.SmoothStep(0, 1, Mathf.Clamp01((arrivalElapsed - index * .022f) / .78f));
            float retract = Mathf.SmoothStep(0, 1, Mathf.Clamp01(
                (bloomElapsed - CompletionHoldSeconds - index * .024f) / RetractSeconds));
            Vector2 position = new Vector2(destination.x,
                Mathf.Lerp(Mathf.Lerp(parkedY, destination.y, descend), parkedY, retract));
            targets[index].position = position;
            // The tip is fixed on the collision target throughout active play. Only
            // the middle of each vine sways, so rendering never suggests a false hit.
            float open = awakened[index] ? Mathf.SmoothStep(0, 1,
                Mathf.Clamp01(wakeAge[index] / FlowerOpenSeconds)) : 0;
            bloomLevels[index] = open;
            float reveal = awakened[index] ? 1 : reveals[index];
            float glowBeat = roundComplete ? Mathf.Sin(Mathf.Clamp01(bloomElapsed / CompletionHoldSeconds) * Mathf.PI) : 0;
            Color tint = PetalTint(index);
            Color budTint = config != null && config.bud != null ? Color.white : tint;
            float budAlpha = reveal * (1 - open);
            buds[index].enabled = budAlpha > .001f;
            buds[index].color = new Color(budTint.r, budTint.g, budTint.b, budAlpha);
            buds[index].transform.localScale = Vector3.one * Mathf.Lerp(1, 1.13f, open);
            buds[index].transform.localRotation = Quaternion.identity;
            flowers[index].enabled = open > .001f;
            flowers[index].color = new Color(tint.r, tint.g, tint.b, open);
            float unfurl = Mathf.Lerp(.45f, 1f, open) + Mathf.Sin(open * Mathf.PI) * .13f;
            flowers[index].transform.localScale = Vector3.one * unfurl;
            flowers[index].transform.localRotation = config != null && config.flower != null
                ? Quaternion.identity
                : Quaternion.Euler(0, 0, index * 19f + roundIndex * 7f - (1 - open) * 25f);
            // Only opened flowers emit. A compact petal-shaped bloom and white
            // heart add radiance without revealing other closed buds nearby.
            float breath = .96f + .04f * Mathf.Sin(presentationTime * 1.9f + index * .73f);
            float openingFlash = Mathf.Exp(-Mathf.Pow((wakeAge[index] - .30f) / .19f, 2)) * open;
            flowerRadiance[index].enabled = open > .001f && flowerRadianceSprite != null;
            flowerRadiance[index].color = new Color(tint.r, tint.g, tint.b,
                open * Mathf.Clamp01(.72f * breath + openingFlash * .22f + glowBeat * .18f));
            flowerRadiance[index].transform.localScale = Vector3.one * (unfurl + openingFlash * .06f + glowBeat * .08f);
            flowerRadiance[index].transform.localRotation = flowers[index].transform.localRotation;
            flowerHearts[index].enabled = open > .001f;
            flowerHearts[index].color = new Color(1f, .99f, .90f, open * (.80f + .12f * openingFlash));
            flowerHearts[index].transform.localScale = Vector3.one * (.25f + openingFlash * .04f + glowBeat * .03f);
            float budHalo = reveal * .12f * (1 - open);
            halos[index].enabled = budHalo > .001f;
            halos[index].color = new Color(tint.r, tint.g, tint.b, budHalo);
            halos[index].transform.localScale = Vector3.one * .42f;
            ApplyVine(index, position + Vector2.up * .16f, top, reveal, open);
        }

        Color PetalTint(int index)
        {
            // Imported paintings keep their authored colors. The native flowers vary
            // quietly between ivory, mint and warm gold from one round to the next.
            if (config != null && config.flower != null) return Color.white;
            float variation = (roundIndex + index / 3) % 3;
            if (variation < 1) return new Color(1f, .97f, .86f);
            if (variation < 2) return new Color(.79f, 1f, .90f);
            return new Color(1f, .89f, .67f);
        }

        void ApplyVine(int index, Vector2 tip, float top, float reveal, float open)
        {
            bool inView = tip.y < top;
            float length = Mathf.Max(0, top - tip.y);
            float anchorX = targetPositions[index].x + Mathf.Sin(index * 1.91f + roundIndex * .32f) * .16f;
            Vector2 anchor = new Vector2(anchorX, top);
            float sway = Mathf.Sin(presentationTime * .72f + index * 1.4f) * .065f;
            Color foliage = config != null ? config.gardenFoliage : new Color(.13f, .31f, .24f);
            Color stemColor = Color.Lerp(foliage * .88f, new Color(.34f, .46f, .30f), open * .25f);
            Vector2 previous = anchor;
            for (int segment = 0; segment < VineSegments; segment++)
            {
                float t = (segment + 1f) / VineSegments;
                Vector2 next = VinePoint(anchor, tip, t, sway, index);
                SpriteRenderer stem = vines[index, segment];
                stem.enabled = inView;
                if (inView)
                {
                    Vector2 delta = next - previous;
                    stem.transform.position = (previous + next) * .5f;
                    stem.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                    stem.transform.localScale = new Vector3(delta.magnitude + .008f, .024f, 1);
                    float distanceFromTip = length * (1 - (t - .5f / VineSegments));
                    float lowerFade = Mathf.SmoothStep(0, 1, Mathf.Clamp01((distanceFromTip - .16f) / .95f));
                    float alpha = .52f * Mathf.Lerp(lowerFade, 1, reveal);
                    stem.color = new Color(stemColor.r, stemColor.g, stemColor.b, alpha);
                }
                previous = next;
            }
            for (int leaf = 0; leaf < LeavesPerVine; leaf++)
            {
                SpriteRenderer view = leaves[index, leaf];
                float t = .22f + leaf * .18f + (index % 2) * .035f;
                float distanceFromTip = length * (1 - t);
                view.enabled = inView && length > .55f;
                if (!view.enabled) continue;
                int side = (index + leaf) % 2 == 0 ? -1 : 1;
                Vector2 attach = VinePoint(anchor, tip, t, sway, index);
                view.transform.position = attach + new Vector2(side * .045f, -.035f);
                view.transform.localRotation = Quaternion.Euler(0, 0, side * 52f);
                view.transform.localScale = new Vector3(.11f, .13f, 1);
                float lowerFade = Mathf.SmoothStep(0, 1, Mathf.Clamp01((distanceFromTip - .18f) / .95f));
                // The leaf texture already carries the foliage palette. Keep
                // that color instead of multiplying it by a second dark green.
                view.color = new Color(.67f, .74f, .63f,
                    .62f * Mathf.Lerp(lowerFade, 1, reveal));
            }
        }

        static Vector2 VinePoint(Vector2 anchor, Vector2 tip, float amount, float sway, int index)
        {
            Vector2 point = Vector2.Lerp(anchor, tip, amount);
            point.x += Mathf.Sin(amount * Mathf.PI) * (sway + Mathf.Sin(amount * Mathf.PI * 2 + index) * .045f);
            return point;
        }

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
                // and edge lighting while keeping the ball and flowers legible.
                float opacity = backdropConfigured ? .24f : .48f;
                pixels[y * width + x] = new Color(dark.r, dark.g, dark.b, center * opacity);
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
