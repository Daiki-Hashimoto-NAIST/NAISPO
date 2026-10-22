using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Naispo.Interaction
{
    /// <summary>Visual-only arena scenery. The court continues to own every physical boundary.</summary>
    public sealed class RallyCyberWorld : MonoBehaviour
    {
        const float FloorDepth = 1f;
        readonly List<Object> ownedResources = new List<Object>();
        GameObject scenery;
        Camera arenaCamera;
        Transform backdrop;
        Sprite whiteSprite;
        SpriteRenderer netLine, netHalo, topCap, floorCap;
        bool visible = true, netConfigured;
        float viewportHeight, viewportAspect, netTopY, netFloorY;
        Vector3 cameraPosition;

        public void Initialize(float halfWidth, float matchFloor, Camera camera)
        {
            if(scenery != null || camera == null || !camera.orthographic) return;
            arenaCamera = camera;
            scenery = CreateChild("Cyber arena scenery", transform);
            scenery.transform.position = new Vector3(camera.transform.position.x, 0, 0);

            var sky = CreateSprite(CreateSkyTexture(), "Cyber atmosphere");
            backdrop = CreateRenderer("Blue black sky", sky, Color.white, -80).transform;
            UpdateBackdrop();
            var white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Cyber line white" };
            white.SetPixel(0, 0, Color.white); white.Apply(false, true);
            whiteSprite = CreateSprite(white, "Cyber line");
            BuildFloor(halfWidth, matchFloor);
            BuildNet();
            if(!netConfigured) SetNet(matchFloor + 1, matchFloor);
            else UpdateNet();
            scenery.SetActive(visible);
        }

        public void SetVisible(bool value)
        {
            visible = value;
            if(scenery != null) scenery.SetActive(value);
        }

        // Coordinates are absolute world-space heights. The caller supplies the scoring floor,
        // which can coincide with the top of the physical net at its lowest setting.
        public void SetNet(float topY, float floorY)
        {
            netConfigured = true;
            netFloorY = floorY;
            netTopY = Mathf.Max(topY, floorY);
            UpdateNet();
        }

        void Update()
        {
            if(!visible || scenery == null || arenaCamera == null) return;
            if(!Mathf.Approximately(viewportHeight, arenaCamera.orthographicSize) ||
               !Mathf.Approximately(viewportAspect, arenaCamera.aspect) ||
               cameraPosition != arenaCamera.transform.position) UpdateBackdrop();
        }

        void UpdateBackdrop()
        {
            viewportHeight = arenaCamera.orthographicSize;
            viewportAspect = arenaCamera.aspect;
            cameraPosition = arenaCamera.transform.position;
            backdrop.position = new Vector3(cameraPosition.x, cameraPosition.y, 0);
            backdrop.localScale = new Vector3(viewportHeight * viewportAspect * 2 + .1f,
                viewportHeight * 2 + .1f, 1);
        }

        void BuildFloor(float halfWidth, float matchFloor)
        {
            float edge = Mathf.Max(.5f, halfWidth - .25f);
            float bottom = matchFloor - FloorDepth;
            float vanishingY = matchFloor + 2.6f;
            float nearDistance = vanishingY - bottom;
            Color gridColor = new Color(.36f, .47f, .61f, .20f);

            // Clip each ray to this shallow floor band before creating any geometry.
            // The vanishing point is only a construction aid and never becomes a visible line.
            for(int i = -6; i <= 6; i++)
            {
                float nearX = i * edge * .25f;
                float farX = nearX * (vanishingY - matchFloor) / nearDistance;
                if(Mathf.Abs(farX) > edge) continue;
                float endX = Mathf.Clamp(nearX, -edge, edge);
                float endY = Mathf.Abs(nearX) <= edge ? bottom
                    : vanishingY - nearDistance * Mathf.Abs(endX / nearX);
                CreateLine("Floor depth line " + i, new Vector2(farX, matchFloor),
                    new Vector2(endX, endY), .012f, gridColor, -5);
            }
            float[] depths = { .16f, .39f, .70f, 1f };
            for(int i = 0; i < depths.Length; i++)
            {
                float y = matchFloor - depths[i];
                Color color = gridColor;
                color.a = Mathf.Lerp(.20f, .10f, depths[i]);
                CreateLine("Floor cross line " + i, new Vector2(-edge, y), new Vector2(edge, y),
                    .012f, color, -5);
            }

            for(int side = -1; side <= 1; side += 2)
            {
                Color color = side < 0 ? new Color(.50f, .91f, .96f, .48f)
                    : new Color(1, .70f, .55f, .48f);
                float x = side * edge;
                CreateLine("Arena corner upright " + side, new Vector2(x, matchFloor + .23f),
                    new Vector2(x, matchFloor), .018f, color, -5);
                CreateLine("Arena corner foot " + side, new Vector2(x, matchFloor),
                    new Vector2(x - side * .24f, matchFloor), .018f, color, -5);
            }
        }

        void BuildNet()
        {
            netLine = CreateRenderer("Fine center net", CreateSprite(CreateNetTexture(false), "Cyber net gradient"),
                Color.white, 3);
            netHalo = CreateRenderer("Center net soft edge", CreateSprite(CreateNetTexture(true), "Cyber net glow"),
                Color.white, 3);
            topCap = CreateRenderer("Center net top cap", whiteSprite, new Color(.66f, .78f, .91f, .76f), 3);
            floorCap = CreateRenderer("Center net floor cap", whiteSprite, new Color(.66f, .78f, .91f, .48f), 3);
        }

        void UpdateNet()
        {
            if(netLine == null) return;
            float height = Mathf.Max(0, netTopY - netFloorY);
            bool hasHeight = height > .001f;
            netLine.enabled = netHalo.enabled = hasHeight;
            netLine.transform.localPosition = netHalo.transform.localPosition = new Vector3(0, netFloorY + height * .5f, 0);
            netLine.transform.localScale = new Vector3(.025f, height, 1);
            netHalo.transform.localScale = new Vector3(.13f, height, 1);
            topCap.enabled = height > .025f;
            topCap.transform.localPosition = new Vector3(0, netTopY, 0);
            topCap.transform.localScale = new Vector3(.16f, .018f, 1);
            floorCap.transform.localPosition = new Vector3(0, netFloorY, 0);
            floorCap.transform.localScale = new Vector3(.16f, .018f, 1);
        }

        void CreateLine(string objectName, Vector2 from, Vector2 to, float width, Color color, int order)
        {
            Vector2 delta = to - from;
            var renderer = CreateRenderer(objectName, whiteSprite, color, order);
            renderer.transform.localPosition = (from + to) * .5f;
            renderer.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            renderer.transform.localScale = new Vector3(delta.magnitude, width, 1);
        }

        Texture2D CreateSkyTexture()
        {
            const int width = 256, height = 144;
            var texture = NewTexture(width, height, "Cyber blue atmosphere");
            var pixels = new Color32[width * height];
            Color dark = new Color(.024f, .043f, .078f);
            Color light = new Color(.071f, .105f, .164f);
            for(int y = 0; y < height; y++)
            for(int x = 0; x < width; x++)
            {
                float nx = ((x + .5f) / width - .5f) * 1.75f;
                float ny = ((y + .5f) / height - .73f) * 1.2f;
                float amount = Mathf.Exp(-(nx * nx + ny * ny) * 3.6f);
                pixels[y * width + x] = Color.Lerp(dark, light, amount * .66f);
            }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        Texture2D CreateNetTexture(bool soft)
        {
            int width = soft ? 32 : 1;
            const int height = 64;
            var texture = NewTexture(width, height, soft ? "Cyber net bloom" : "Cyber net light");
            var pixels = new Color32[width * height];
            for(int y = 0; y < height; y++)
            for(int x = 0; x < width; x++)
            {
                float t = y / (float)(height - 1);
                Color color = Color.Lerp(new Color(.49f, .61f, .73f, .18f),
                    new Color(.78f, .85f, .93f, .90f), t);
                if(soft)
                {
                    float horizontal = Mathf.Abs((x + .5f) / width * 2 - 1);
                    color.a *= Mathf.Pow(1 - horizontal, 2) * .10f;
                }
                pixels[y * width + x] = color;
            }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        static Texture2D NewTexture(int width, int height, string textureName)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = textureName, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
        }

        Sprite CreateSprite(Texture2D texture, string spriteName)
        {
            ownedResources.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                Vector2.one * .5f, texture.width, 0, SpriteMeshType.FullRect);
            sprite.name = spriteName;
            ownedResources.Add(sprite);
            return sprite;
        }

        SpriteRenderer CreateRenderer(string objectName, Sprite sprite, Color color, int order)
        {
            var child = CreateChild(objectName, scenery.transform);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.color = color; renderer.sortingOrder = order;
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = Vector2.one;
            return renderer;
        }

        GameObject CreateChild(string childName, Transform parent)
        {
            var child = new GameObject(childName);
            SceneManager.MoveGameObjectToScene(child, gameObject.scene);
            child.transform.SetParent(parent, false);
            return child;
        }

        void OnDestroy()
        {
            Release(scenery);
            for(int i = ownedResources.Count - 1; i >= 0; i--) Release(ownedResources[i]);
            ownedResources.Clear();
        }

        static void Release(Object resource)
        {
            if(resource == null) return;
            if(Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
        }
    }
}
