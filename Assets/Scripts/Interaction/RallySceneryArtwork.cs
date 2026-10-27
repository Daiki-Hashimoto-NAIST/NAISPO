using UnityEngine;
using UnityEngine.SceneManagement;

namespace Naispo.Interaction
{
    /// <summary>
    /// Small shared helpers for deterministic, render-only architecture.
    /// Generated textures and sprites belong to the caller's owned-resource list.
    /// </summary>
    public static class RallySceneryArtwork
    {
        /// <summary>
        /// Creates a material only for a validated, un-atlased painted backdrop.
        /// The caller owns the returned material and blurred texture; the source image stays untouched.
        /// Keeping the shader in Resources also retains it in player builds.
        /// </summary>
        public static Material CreateBackdropMaterial(Sprite sprite, RallyVisualConfig config,
            out RenderTexture blurredTexture, RallyBackdropDepthProfile depthProfile = RallyBackdropDepthProfile.Uniform)
        {
            blurredTexture = null;
            if (sprite == null || sprite.texture == null || sprite.packed || config == null) return null;
            var shader = Resources.Load<Shader>("RallyBackdropSoftening");
            if (shader == null || !shader.isSupported) return null;
            var material = new Material(shader)
            {
                name = "Painted backdrop softening",
                hideFlags = HideFlags.DontSave
            };
            material.SetFloat("_Saturation", Mathf.Clamp01(config.backgroundSaturation));
            material.SetFloat("_Brightness", Mathf.Clamp(config.backgroundBrightness, .2f, 1));
            material.SetFloat("_HighlightCompression", Mathf.Clamp01(config.backgroundHighlightCompression));
            Rect rect = sprite.rect;
            float width = sprite.texture.width, height = sprite.texture.height;
            var uvRect = new Vector4((rect.xMin + .5f) / width,
                (rect.yMin + .5f) / height, (rect.xMax - .5f) / width, (rect.yMax - .5f) / height);
            material.SetVector("_UvRect", uvRect);
            // Bake once at source resolution with a radius that grows along the
            // authored scene depth, while preserving crisp foreground surfaces.
            float sigma = Mathf.Clamp(config.backgroundBlurPixels, 0, 6) * rect.width / 1280f;
            blurredTexture = CreateBlurredBackdrop(sprite, uvRect, sigma, depthProfile);
            material.SetFloat("_HasBlurredTex", blurredTexture != null ? 1 : 0);
            if (blurredTexture != null) material.SetTexture("_BlurredTex", blurredTexture);
            return material;
        }

        static RenderTexture CreateBlurredBackdrop(Sprite sprite, Vector4 uvRect, float sigma,
            RallyBackdropDepthProfile depthProfile)
        {
            if (sigma < .01f || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)) return null;
            var shader = Resources.Load<Shader>("RallyBackdropGaussian");
            if (shader == null || !shader.isSupported) return null;
            Texture2D source = sprite.texture;
            RenderTexture previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            RenderTexture temporary = null, result = null;
            Material filter = null;
            Texture2D depthTexture = null;
            bool completed = false;
            try
            {
                filter = new Material(shader) { hideFlags = HideFlags.DontSave };
                filter.SetVector("_UvRect", uvRect);
                filter.SetFloat("_Sigma", sigma);
                filter.SetInt("_KernelRadius", Mathf.Min(32, Mathf.CeilToInt(sigma * 3)));
                if (depthProfile != RallyBackdropDepthProfile.Uniform)
                    depthTexture = RallyBackdropDepth.CreateTexture(sprite, depthProfile);
                filter.SetFloat("_HasDepthTex", depthTexture != null ? 1 : 0);
                if (depthTexture != null) filter.SetTexture("_DepthTex", depthTexture);
                result = new RenderTexture(source.width, source.height, 0,
                    RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = source.name + " distant room defocus",
                    hideFlags = HideFlags.DontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                if (!result.Create()) return null;
                temporary = RenderTexture.GetTemporary(source.width, source.height, 0,
                    RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                temporary.wrapMode = TextureWrapMode.Clamp;
                temporary.filterMode = FilterMode.Bilinear;
                GL.sRGBWrite = false;
                // Pass one premultiplies source alpha; pass two filters that result
                // without multiplying again. Both passes sample every source texel.
                Graphics.Blit(source, temporary, filter, 0);
                Graphics.Blit(temporary, result, filter, 1);
                completed = true;
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgbWrite;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
                if (filter != null)
                {
                    if (Application.isPlaying) Object.Destroy(filter); else Object.DestroyImmediate(filter);
                }
                if (depthTexture != null)
                {
                    if (Application.isPlaying) Object.Destroy(depthTexture); else Object.DestroyImmediate(depthTexture);
                }
                if (!completed && result != null)
                {
                    result.Release();
                    if (Application.isPlaying) Object.Destroy(result); else Object.DestroyImmediate(result);
                }
            }
        }

        public static Texture2D NewTexture(int width, int height, string textureName)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = textureName,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }

        public static Sprite CreateSprite(Texture2D texture, string spriteName)
        {
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                Vector2.one * .5f, texture.width, 0, SpriteMeshType.FullRect);
            sprite.name = spriteName;
            return sprite;
        }

        /// <summary>
        /// Wraps any sprite in a centered proxy whose largest side is maxWorldSize.
        /// The source image rect is preserved, and the
        /// wrapper pivot is recentered so imported art sits on the game's logical point
        /// without inheriting the asset's original pivot or pixels-per-unit.
        /// </summary>
        public static Sprite CreateNormalizedSprite(Sprite source, float maxWorldSize, string wrapperName)
        {
            if (source == null || source.texture == null || source.packed || maxWorldSize <= 0) return null;
            Texture2D texture = source.texture;
            // Packed atlas sprites may be rotated or tightly trimmed. Keep the
            // procedural fallback for those instead of displaying another atlas region.
            Rect rect = source.rect;
            if (rect.width < .001f || rect.height < .001f || rect.x < 0 || rect.y < 0 ||
                rect.xMax > texture.width || rect.yMax > texture.height) return null;
            float pixelsPerUnit = Mathf.Max(rect.width, rect.height) / maxWorldSize;
            try
            {
                // FullRect avoids tight-mesh/hull requirements on imported textures.
                // Recentering the pivot keeps even a tight or offset source rect on the
                // caller's physics/logical point.
                var wrapper = Sprite.Create(texture, rect, Vector2.one * .5f, pixelsPerUnit,
                    0, SpriteMeshType.FullRect);
                wrapper.name = wrapperName;
                return wrapper;
            }
            catch (System.ArgumentException)
            {
                // Packed/rotated atlas rects are not all representable by Sprite.Create.
                // A safe documented fallback keeps the slot empty rather than throwing.
                return null;
            }
        }

        /// <summary>
        /// Sizes a Simple sprite so its rendered quad covers the viewport. When
        /// preserveAspect is true the sprite is uniformly scaled to cover (crop is
        /// acceptable); otherwise it stretches to the exact viewport. The pivot
        /// offset is removed so imported artwork stays centered on the camera.
        /// </summary>
        public static void FitSpriteToViewport(Transform target, Sprite sprite, Vector3 viewportCenter,
            float viewportWidth, float viewportHeight, bool preserveAspect, float verticalAlignment = .5f)
        {
            if (target == null || sprite == null) return;
            Vector3 boundsSize = sprite.bounds.size;
            float safeWidth = Mathf.Max(.01f, boundsSize.x);
            float safeHeight = Mathf.Max(.01f, boundsSize.y);
            Vector3 scale;
            if (preserveAspect)
            {
                float cover = Mathf.Max(viewportWidth / safeWidth, viewportHeight / safeHeight) * 1.01f;
                scale = new Vector3(cover, cover, 1);
            }
            else
            {
                scale = new Vector3(viewportWidth / safeWidth * 1.01f,
                    viewportHeight / safeHeight * 1.01f, 1);
            }
            Vector3 pivotOffset = Vector3.Scale(sprite.bounds.center, scale);
            // Only distribute the excess height, so every alignment still covers the
            // viewport. The default keeps existing centered art and veils unchanged.
            float verticalOffset = preserveAspect ? (.5f - Mathf.Clamp01(verticalAlignment)) *
                Mathf.Max(0, safeHeight * scale.y - viewportHeight) : 0;
            target.localScale = scale;
            target.position = new Vector3(viewportCenter.x - pivotOffset.x,
                viewportCenter.y - pivotOffset.y + verticalOffset, 0);
        }

        public static SpriteRenderer AddRenderer(Transform parent, string objectName, Sprite sprite,
            Color color, int order, bool simple = false)
        {
            var child = new GameObject(objectName);
            SceneManager.MoveGameObjectToScene(child, parent.gameObject.scene);
            child.transform.SetParent(parent, false);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            if (!simple)
            {
                // Procedural rects and strokes are authored as unit quads.
                renderer.drawMode = SpriteDrawMode.Sliced;
                renderer.size = Vector2.one;
            }
            // Imported artwork is left in the renderer's default Simple mode. Creating
            // it as Sliced and switching later can keep the cached unit-quad bounds,
            // so the displayed geometry must start Simple.
            return renderer;
        }

        public static SpriteRenderer AddQuad(Transform parent, string objectName, Sprite sprite,
            Color color, int order, Vector2 position, Vector2 size, float degrees = 0)
        {
            var renderer = AddRenderer(parent, objectName, sprite, color, order);
            renderer.transform.localPosition = position;
            renderer.transform.localRotation = Quaternion.Euler(0, 0, degrees);
            renderer.transform.localScale = new Vector3(size.x, size.y, 1);
            return renderer;
        }

        public static void AddArch(Transform parent, string prefix, Sprite quad, Color color, int order,
            Vector2 center, float radiusX, float radiusY, int segments, float width,
            float startDegrees = 12f, float endDegrees = 168f)
        {
            segments = Mathf.Max(2, segments);
            Vector2 previous = center + new Vector2(Mathf.Cos(startDegrees * Mathf.Deg2Rad) * radiusX,
                Mathf.Sin(startDegrees * Mathf.Deg2Rad) * radiusY);
            for (int i = 1; i <= segments; i++)
            {
                float angle = Mathf.Lerp(startDegrees, endDegrees, i / (float)segments) * Mathf.Deg2Rad;
                Vector2 point = center + new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
                AddStroke(parent, prefix + " rib " + i, quad, color, order, previous, point, width);
                previous = point;
            }
        }

        public static void AddStroke(Transform parent, string objectName, Sprite quad, Color color,
            int order, Vector2 from, Vector2 to, float width)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < .001f) return;
            var renderer = AddRenderer(parent, objectName, quad, color, order);
            renderer.transform.localPosition = (from + to) * .5f;
            renderer.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            renderer.transform.localScale = new Vector3(length, width, 1);
        }

        public static void AddColumn(Transform parent, string prefix, Sprite quad, Color stone, Color metal,
            int order, float x, float bottom, float top, float width)
        {
            AddQuad(parent, prefix + " shaft", quad, stone, order, new Vector2(x, (bottom + top) * .5f),
                new Vector2(width, top - bottom));
            AddQuad(parent, prefix + " base", quad, metal, order + 1, new Vector2(x, bottom + .10f),
                new Vector2(width * 2.5f, .07f));
            AddQuad(parent, prefix + " capital", quad, metal, order + 1, new Vector2(x, top - .12f),
                new Vector2(width * 2.2f, .06f));
        }

        /// <summary>Move an object into the owner scene before parenting it.</summary>
        public static GameObject CreateChild(Transform parent, string objectName)
        {
            var child = new GameObject(objectName);
            SceneManager.MoveGameObjectToScene(child, parent.gameObject.scene);
            child.transform.SetParent(parent, false);
            return child;
        }
    }
}
