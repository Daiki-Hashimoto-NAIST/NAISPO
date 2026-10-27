using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Naispo.Interaction
{
    /// <summary>
    /// Render-only violet/bronze dueling hall. The narrow center net mirrors the
    /// physical collider's exact bottom and top; no scenery geometry is physical.
    /// </summary>
    [ExecuteAlways]
    public sealed class RallyCyberWorld : MonoBehaviour
    {
        const float NetLineWidth = .024f;
        const float NetHaloWidth = .072f;
        readonly List<Object> ownedResources = new List<Object>();
        RallyVisualConfig config;
        GameObject scenery;
        Camera arenaCamera;
        Transform backdrop, veil;
        Sprite backdropSprite, veilSprite, lineSprite;
        SpriteRenderer netLine, netHalo, topCap, floorCap;
        bool visible = true, netConfigured, backdropConfigured, destroyed;
        float viewportHeight, viewportAspect, netTopY, netFloorY;
        Vector3 cameraPosition;

        public float NetTop => netTopY;
        public float NetFloor => netFloorY;
        public float NetVisualHeight => Mathf.Max(0, netTopY - netFloorY);
        public bool IsInitialized => scenery != null;
        public bool UsesConfiguredBackground => backdropConfigured;
        public int OwnedResourceCount => ownedResources.Count;
        public Transform Background => backdrop;
        public Sprite BackgroundSprite => backdropSprite;

        public void Initialize(float halfWidth, float matchFloor, Camera camera, RallyVisualConfig visualConfig = null)
        {
            if (scenery != null || camera == null || !camera.orthographic) return;
            config = visualConfig;
            arenaCamera = camera;
            scenery = CreateChild("Dueling hall scenery", transform);
            scenery.transform.position = new Vector3(camera.transform.position.x, 0, 0);

            lineSprite = CreateOwnedTextureSprite(NewSolidTexture(), "Dueling hall line");
            BuildBackdrop(camera);
            // Supplied hall artwork replaces only scenery, never the physical net.
            if (!backdropConfigured) BuildHall(halfWidth, matchFloor, camera);
            BuildNet();
            if (!netConfigured) SetNet(matchFloor + 2, matchFloor - 1);
            scenery.SetActive(visible);
        }

        public void SetVisible(bool value)
        {
            visible = value;
            if (scenery != null) scenery.SetActive(value);
            if (value) RefreshLayout();
        }

        // Absolute world-space heights. The caller supplies the physical net collider's
        // actual bottom and top, including the minimum-height case.
        public void SetNet(float topY, float floorY)
        {
            netConfigured = true;
            netFloorY = floorY;
            netTopY = Mathf.Max(topY, floorY);
            UpdateNet();
        }

        public void RefreshLayout()
        {
            if (arenaCamera == null || scenery == null) return;
            viewportHeight = arenaCamera.orthographicSize;
            viewportAspect = arenaCamera.aspect;
            cameraPosition = arenaCamera.transform.position;
            float width = viewportHeight * viewportAspect * 2, height = viewportHeight * 2;
            Vector3 viewportCenter = new Vector3(cameraPosition.x, cameraPosition.y, 0);
            if (backdrop != null)
            {
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
            if (!visible || scenery == null || arenaCamera == null) return;
            if (!Mathf.Approximately(viewportHeight, arenaCamera.orthographicSize) ||
                !Mathf.Approximately(viewportAspect, arenaCamera.aspect) ||
                cameraPosition != arenaCamera.transform.position) RefreshLayout();
        }

        void BuildBackdrop(Camera camera)
        {
            Sprite configured = Normalized(config != null ? config.battleBackground : null, 1f, "Configured dueling hall background");
            backdropConfigured = configured != null;
            backdropSprite = configured ?? CreateOwnedTextureSprite(CreateSkyTexture(), "Violet dueling hall background");
            var background = RallySceneryArtwork.AddRenderer(scenery.transform, "Violet dueling hall background",
                backdropSprite, Color.white, -90, simple: true);
            if (backdropConfigured)
            {
                var material = Own(RallySceneryArtwork.CreateBackdropMaterial(backdropSprite, config));
                if (material != null) background.sharedMaterial = material;
            }
            backdrop = background.transform;

            veilSprite = CreateOwnedTextureSprite(CreateVeilTexture(), "Dueling hall center veil");
            var veilRenderer = RallySceneryArtwork.AddRenderer(scenery.transform, "Dark dueling veil",
                veilSprite, Color.white, -89, simple: true);
            veil = veilRenderer.transform;
            RefreshLayout();
        }

        void BuildHall(float halfWidth, float matchFloor, Camera camera)
        {
            float bottom = camera.transform.position.y - camera.orthographicSize;
            float top = camera.transform.position.y + camera.orthographicSize * .98f;
            float height = Mathf.Max(2, top - bottom);
            Color deep = config != null ? config.battleDeep : new Color(.043f, .027f, .074f);
            Color violet = config != null ? config.battleViolet : new Color(.53f, .33f, .74f);
            Color glass = config != null ? config.battleGlass : new Color(.27f, .19f, .43f);
            Color bronze = config != null ? config.battleBronze : new Color(.63f, .43f, .24f);
            Color dimBronze = new Color(bronze.r, bronze.g, bronze.b, .34f);

            // Side windows and pointed bronze ribs in the same architectural family.
            for (int side = -1; side <= 1; side += 2)
            {
                float outer = side * (halfWidth - .42f);
                float inner = side * halfWidth * .30f;
                float peak = matchFloor + height * .70f;
                AddPointedArch("Hall arch " + side, outer, inner, peak, matchFloor - .55f,
                    new Color(bronze.r, bronze.g, bronze.b, .28f), 11);
                AddPointedArch("Hall inner arch " + side, outer + side * .10f, inner - side * .10f,
                    peak - height * .06f, matchFloor - .28f, new Color(violet.r, violet.g, violet.b, .18f), 9);
                RallySceneryArtwork.AddQuad(scenery.transform, "Hall glass " + side, lineSprite,
                    new Color(glass.r, glass.g, glass.b, .20f), -72,
                    new Vector2((outer + inner) * .5f, matchFloor + height * .12f),
                    new Vector2(Mathf.Abs(inner - outer) * .88f, height * .46f));
                for (int division = 1; division < 3; division++)
                {
                    float x = Mathf.Lerp(outer, inner, division / 3f);
                    RallySceneryArtwork.AddQuad(scenery.transform, "Hall mullion " + side + " " + division,
                        lineSprite, new Color(bronze.r, bronze.g, bronze.b, .22f), -60,
                        new Vector2(x, matchFloor + height * .06f), new Vector2(.014f, height * .30f));
                }
                RallySceneryArtwork.AddColumn(scenery.transform, "Hall column " + side, lineSprite,
                    new Color(glass.r * .60f, glass.g * .60f, glass.b * .60f, .42f), dimBronze, -58,
                    outer, matchFloor - .70f, top, .13f);
                RallySceneryArtwork.AddColumn(scenery.transform, "Hall inner column " + side, lineSprite,
                    new Color(violet.r * .40f, violet.g * .40f, violet.b * .40f, .32f), dimBronze, -58,
                    inner, matchFloor - .65f, top * .94f, .075f);
                // Sconces are pure sprites: no Light component and no gameplay illumination.
                for (int level = 0; level < 2; level++)
                {
                    float x = outer + side * .24f;
                    float y = matchFloor + height * (.20f + level * .25f);
                    RallySceneryArtwork.AddQuad(scenery.transform, "Sconce stem " + side + " " + level,
                        lineSprite, new Color(bronze.r, bronze.g, bronze.b, .28f), -54,
                        new Vector2(x, y - .10f), new Vector2(.016f, .20f));
                    RallySceneryArtwork.AddQuad(scenery.transform, "Sconce cup " + side + " " + level,
                        lineSprite, new Color(bronze.r, bronze.g, bronze.b, .38f), -53,
                        new Vector2(x, y + .02f), new Vector2(.10f, .10f));
                    RallySceneryArtwork.AddQuad(scenery.transform, "Sconce flame " + side + " " + level,
                        lineSprite, new Color(violet.r, violet.g, violet.b, .20f), -52,
                        new Vector2(x, y + .12f), new Vector2(.045f, .13f));
                }
            }

            // Quiet polished floor with a single bronze inlay and peripheral rune arcs.
            float floorDepth = Mathf.Max(.9f, matchFloor - bottom + .25f);
            RallySceneryArtwork.AddQuad(scenery.transform, "Hall floor", lineSprite,
                new Color(deep.r * .55f, deep.g * .55f, deep.b * .62f, .58f), -66,
                new Vector2(0, matchFloor - floorDepth * .5f), new Vector2(halfWidth * 2, floorDepth));
            RallySceneryArtwork.AddQuad(scenery.transform, "Hall threshold", lineSprite,
                new Color(bronze.r, bronze.g, bronze.b, .26f), -55, new Vector2(0, matchFloor + .006f),
                new Vector2(halfWidth * 1.86f, .020f));
            AddFloorRune(halfWidth, matchFloor, bronze);
        }

        void AddFloorRune(float halfWidth, float matchFloor, Color bronze)
        {
            float radiusX = Mathf.Min(halfWidth * .28f, 2.4f), radiusY = .36f;
            Vector2 center = new Vector2(0, matchFloor - .48f);
            Vector2 previous = center + new Vector2(radiusX, 0);
            for (int i = 1; i <= 18; i++)
            {
                float angle = i * Mathf.PI * 2 / 18;
                Vector2 point = center + new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
                RallySceneryArtwork.AddStroke(scenery.transform, "Floor rune " + i, lineSprite,
                    new Color(bronze.r, bronze.g, bronze.b, .11f), -54, previous, point, .014f);
                previous = point;
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

        void BuildNet()
        {
            netLine = RallySceneryArtwork.AddRenderer(scenery.transform, "Fine center net",
                CreateOwnedTextureSprite(CreateNetTexture(false), "Dueling net gradient"), Color.white, 3);
            netHalo = RallySceneryArtwork.AddRenderer(scenery.transform, "Center net soft edge",
                CreateOwnedTextureSprite(CreateNetTexture(true), "Dueling net glow"), Color.white, 3);
            topCap = RallySceneryArtwork.AddRenderer(scenery.transform, "Center net top cap", lineSprite,
                new Color(.94f, .88f, .78f, .96f), 3);
            floorCap = RallySceneryArtwork.AddRenderer(scenery.transform, "Center net floor cap", lineSprite,
                new Color(.83f, .77f, .94f, .86f), 3);
        }

        void UpdateNet()
        {
            if (netLine == null) return;
            float height = Mathf.Max(0, netTopY - netFloorY);
            bool hasHeight = height > .001f;
            netLine.enabled = netHalo.enabled = hasHeight;
            netLine.transform.localPosition = netHalo.transform.localPosition =
                new Vector3(0, netFloorY + height * .5f, 0);
            netLine.transform.localScale = new Vector3(NetLineWidth, height, 1);
            netHalo.transform.localScale = new Vector3(NetHaloWidth, height, 1);
            topCap.enabled = hasHeight;
            topCap.transform.localPosition = new Vector3(0, netTopY, 0);
            topCap.transform.localScale = new Vector3(.14f, .018f, 1);
            floorCap.enabled = hasHeight;
            floorCap.transform.localPosition = new Vector3(0, netFloorY, 0);
            floorCap.transform.localScale = new Vector3(.14f, .018f, 1);
        }

        Texture2D CreateSkyTexture()
        {
            const int width = 512, height = 288;
            var texture = RallySceneryArtwork.NewTexture(width, height, "Violet dueling hall atmosphere");
            var pixels = new Color32[width * height];
            Color deep = config != null ? config.battleDeep : new Color(.043f, .027f, .074f);
            Color violet = config != null ? config.battleViolet : new Color(.53f, .33f, .74f);
            Color bronze = config != null ? config.battleBronze : new Color(.63f, .43f, .24f);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float nx = (x + .5f) / width * 2 - 1;
                float ny = (y + .5f) / height;
                Color color = Color.Lerp(deep * .50f, deep * 1.50f, Mathf.SmoothStep(0, 1, ny));
                float side = Mathf.Pow(Mathf.Abs(nx), 2.8f);
                color += new Color(violet.r, violet.g, violet.b, 0) * (side * .23f * (.58f + .42f * ny));
                color += new Color(bronze.r, bronze.g, bronze.b, 0) * (Mathf.Pow(Mathf.Abs(nx), 7f) * .11f);
                float center = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .48f, Mathf.Abs(nx)));
                color *= 1 - center * .38f;
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
            var texture = RallySceneryArtwork.NewTexture(width, height, "Dueling hall center veil");
            var pixels = new Color32[width * height];
            Color dark = config != null ? config.battleDeep : new Color(.043f, .027f, .074f);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float nx = Mathf.Abs((x + .5f) / width * 2 - 1);
                float ny = Mathf.Abs((y + .5f) / height * 2 - 1);
                float center = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.18f, .92f, Mathf.Max(nx * .82f, ny)));
                float opacity = backdropConfigured ? .30f : .50f;
                pixels[y * width + x] = new Color(dark.r, dark.g, dark.b, center * opacity);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        Texture2D CreateNetTexture(bool soft)
        {
            int width = soft ? 24 : 1;
            const int height = 64;
            var texture = RallySceneryArtwork.NewTexture(width, height, soft ? "Dueling net bloom" : "Dueling net light");
            var pixels = new Color32[width * height];
            Color violet = config != null ? config.battleViolet : new Color(.53f, .33f, .74f);
            Color ivory = config != null ? config.ballIvory : new Color(1f, .995f, .96f);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float t = y / (float)(height - 1);
                Color color = Color.Lerp(Color.Lerp(violet, ivory, .52f), ivory, t * .62f);
                color.a = Mathf.Lerp(.68f, .94f, t);
                if (soft)
                {
                    float horizontal = Mathf.Abs((x + .5f) / width * 2 - 1);
                    color.a *= Mathf.Pow(1 - horizontal, 2) * .09f;
                }
                pixels[y * width + x] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        Texture2D NewSolidTexture()
        {
            var texture = RallySceneryArtwork.NewTexture(1, 1, "Dueling hall solid");
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
            if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
        }
    }
}
