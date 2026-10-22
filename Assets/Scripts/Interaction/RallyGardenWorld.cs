using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Naispo.Interaction
{
    /// <summary>Decorative scenery for the star garden. It never participates in game physics.</summary>
    public sealed class RallyGardenWorld : MonoBehaviour
    {
        const int FireflyCount = 10;
        readonly List<Object> ownedResources = new List<Object>();
        readonly SpriteRenderer[] fireflies = new SpriteRenderer[FireflyCount];
        readonly SpriteRenderer[] fireflyHalos = new SpriteRenderer[FireflyCount];
        readonly Vector3[] fireflyOrigins = new Vector3[FireflyCount];
        readonly float[] fireflyPhases = new float[FireflyCount];
        GameObject scenery;
        Camera gardenCamera;
        Transform backdrop;
        Sprite glowSprite;
        bool visible = true;
        float viewportHeight, viewportAspect;

        public void Initialize(float halfWidth, float floorY, Camera camera)
        {
            if(scenery != null || camera == null || !camera.orthographic) return;
            gardenCamera = camera;
            scenery = CreateChild("Light garden", transform);
            float centerX = camera.transform.position.x;
            scenery.transform.position = new Vector3(centerX, 0, 0);

            Sprite backgroundSprite = CreateSprite(CreateBackgroundTexture(), "Garden atmosphere");
            backdrop = CreateSpriteRenderer("Deep green sky", backgroundSprite, Color.white, -80).transform;
            UpdateBackdrop();

            glowSprite = CreateSprite(CreateGlowTexture(), "Garden soft light");
            BuildGround(halfWidth, floorY, camera);
            BuildFireflies(halfWidth, floorY);
            scenery.SetActive(visible);
        }

        public void SetVisible(bool value)
        {
            visible = value;
            if(scenery != null) scenery.SetActive(value);
        }

        void Update()
        {
            if(!visible || scenery == null || gardenCamera == null) return;
            // The court stays in world space; only the sky follows a resized camera viewport.
            if(!Mathf.Approximately(viewportHeight, gardenCamera.orthographicSize) ||
               !Mathf.Approximately(viewportAspect, gardenCamera.aspect)) UpdateBackdrop();

            float time = Time.unscaledTime;
            for(int i = 0; i < FireflyCount; i++)
            {
                float phase = fireflyPhases[i];
                Vector3 origin = fireflyOrigins[i];
                Vector3 drift = new Vector3(Mathf.Sin(time * .17f + phase) * .12f,
                    Mathf.Sin(time * .23f + phase * 1.7f) * .16f, 0);
                fireflies[i].transform.localPosition = origin + drift;
                fireflyHalos[i].transform.localPosition = origin + drift;
                float pulse = .5f + .5f * Mathf.Sin(time * .58f + phase);
                fireflies[i].color = new Color(.78f, .87f, .52f, .12f + pulse * .23f);
                fireflyHalos[i].color = new Color(.62f, .83f, .42f, .025f + pulse * .045f);
            }
        }

        void UpdateBackdrop()
        {
            viewportHeight = gardenCamera.orthographicSize;
            viewportAspect = gardenCamera.aspect;
            Vector3 position = gardenCamera.transform.position;
            backdrop.position = new Vector3(position.x, position.y, 0);
            // A small overscan prevents a transparent seam at the viewport edge.
            backdrop.localScale = new Vector3(viewportHeight * viewportAspect * 2 + .1f,
                viewportHeight * 2 + .1f, 1);
        }

        void BuildGround(float halfWidth, float floorY, Camera camera)
        {
            var texture = new Texture2D(128, 32, TextureFormat.RGBA32, false)
            {
                name = "Garden ground mist", wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[texture.width * texture.height];
            for(int y = 0; y < texture.height; y++)
            for(int x = 0; x < texture.width; x++)
            {
                float horizontal = Mathf.Sin(Mathf.PI * (x + .5f) / texture.width);
                float vertical = Mathf.Pow(1 - (y + .5f) / texture.height, 1.4f);
                pixels[y * texture.width + x] = new Color(.18f, .31f, .22f, horizontal * vertical * .24f);
            }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            Sprite sprite = CreateSprite(texture, "Garden floor fade");
            var floor = CreateSpriteRenderer("Ground mist", sprite, Color.white, -70);
            float height = Mathf.Max(.6f, floorY - (camera.transform.position.y - camera.orthographicSize) + .3f);
            floor.transform.localPosition = new Vector3(0, floorY - height * .5f + .12f, 0);
            floor.transform.localScale = new Vector3(halfWidth * 2, height, 1);

            var leftPool = CreateSpriteRenderer("Left ground reflection", glowSprite,
                new Color(.64f, .94f, .81f, .065f), -65);
            leftPool.transform.localPosition = new Vector3(-halfWidth * .52f, floorY - .025f, 0);
            leftPool.transform.localScale = new Vector3(2.2f, .19f, 1);
            var rightPool = CreateSpriteRenderer("Right ground reflection", glowSprite,
                new Color(.94f, .77f, .52f, .055f), -65);
            rightPool.transform.localPosition = new Vector3(halfWidth * .52f, floorY - .025f, 0);
            rightPool.transform.localScale = new Vector3(2.2f, .19f, 1);
        }

        void BuildFireflies(float halfWidth, float floorY)
        {
            var random = new System.Random(2701);
            for(int i = 0; i < FireflyCount; i++)
            {
                int side = i < FireflyCount / 2 ? -1 : 1;
                float x = side * halfWidth * (.77f + (float)random.NextDouble() * .18f);
                float y = floorY + .38f + (float)random.NextDouble() * 1.7f;
                fireflyOrigins[i] = new Vector3(x, y, 0);
                fireflyPhases[i] = (float)random.NextDouble() * Mathf.PI * 2;
                fireflies[i] = CreateSpriteRenderer("Firefly " + i, glowSprite,
                    new Color(.78f, .87f, .52f, .25f), -44);
                fireflies[i].transform.localScale = Vector3.one * .055f;
                fireflies[i].transform.localPosition = fireflyOrigins[i];
                fireflyHalos[i] = CreateSpriteRenderer("Firefly halo " + i, glowSprite,
                    new Color(.62f, .83f, .42f, .05f), -45);
                fireflyHalos[i].transform.localScale = Vector3.one * .24f;
                fireflyHalos[i].transform.localPosition = fireflyOrigins[i];
            }
        }

        Texture2D CreateBackgroundTexture()
        {
            const int width = 256, height = 144;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Garden green atmosphere", wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[width * height];
            Color dark = new Color(.027f, .075f, .055f);
            Color light = new Color(.075f, .16f, .105f);
            for(int y = 0; y < height; y++)
            for(int x = 0; x < width; x++)
            {
                float nx = ((x + .5f) / width - .5f) * 1.7f;
                float ny = ((y + .5f) / height - .70f) * 1.18f;
                float glow = Mathf.Exp(-(nx * nx + ny * ny) * 3.8f);
                pixels[y * width + x] = Color.Lerp(dark, light, glow * .72f);
            }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        Texture2D CreateGlowTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Garden round light", wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            for(int y = 0; y < size; y++)
            for(int x = 0; x < size; x++)
            {
                float radius = Vector2.Distance(new Vector2(x + .5f, y + .5f), Vector2.one * size * .5f) / (size * .5f);
                pixels[y * size + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - radius), 2));
            }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        Sprite CreateSprite(Texture2D texture, string spriteName)
        {
            Own(texture);
            var sprite = Own(Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                Vector2.one * .5f, texture.width, 0, SpriteMeshType.FullRect));
            sprite.name = spriteName;
            // Normalize every generated sprite to a one-by-one world-space quad.
            // Non-square textures are normalized in the renderer helper below.
            return sprite;
        }

        SpriteRenderer CreateSpriteRenderer(string objectName, Sprite sprite, Color color, int order)
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

        T Own<T>(T resource) where T : Object
        {
            ownedResources.Add(resource);
            return resource;
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
