using UnityEngine;
using UnityEngine.Rendering;

namespace Naispo.Interaction
{
    /// <summary>
    /// Local flower emission and cached petal-shaped bloom. The caller owns each
    /// returned material, sprite and texture. Source artwork is never modified.
    /// </summary>
    public static class RallyFlowerRadianceArtwork
    {
        public static Material CreateEmissionMaterial(float intensity) =>
            CreateMaterial("Light flower emission", intensity, BlendMode.OneMinusSrcAlpha);

        public static Material CreateBloomMaterial(float intensity) =>
            CreateMaterial("Light flower petal bloom", intensity, BlendMode.One);

        static Material CreateMaterial(string name, float intensity, BlendMode destination)
        {
            var shader = Resources.Load<Shader>("RallyFlowerEmission");
            if (shader == null || !shader.isSupported) return null;
            var material = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
            material.SetFloat("_Emission", Mathf.Max(0, intensity));
            material.SetFloat("_DstBlend", (float)destination);
            return material;
        }

        /// <summary>
        /// Builds two soft, compact skirts from the actual source alpha contour.
        /// The crop is sampled once on the GPU, including for non-readable assets.
        /// Padding extends equally in world units around the source; its pivot,
        /// aspect and original world size remain aligned when the sprites overlap.
        /// No scene light, camera bloom or continuously updated texture is needed.
        /// </summary>
        public static Sprite CreateBloomSprite(Sprite source, out Texture2D ownedTexture)
        {
            ownedTexture = null;
            if (source == null || source.texture == null || source.packed) return null;
            Rect crop = source.rect;
            Texture2D sourceTexture = source.texture;
            if (crop.width <= 0 || crop.height <= 0 || crop.x < 0 || crop.y < 0 ||
                crop.xMax > sourceTexture.width || crop.yMax > sourceTexture.height ||
                source.pixelsPerUnit <= 0) return null;

            // Bound initialization cost even if an imported flower is a large image.
            float samplingScale = Mathf.Min(1, 192f / Mathf.Max(crop.width, crop.height));
            float cropWidth = crop.width * samplingScale, cropHeight = crop.height * samplingScale;
            int sourceWidth = Mathf.CeilToInt(cropWidth), sourceHeight = Mathf.CeilToInt(cropHeight);
            int padding = Mathf.CeilToInt(Mathf.Max(cropWidth, cropHeight) * .30f);
            int width = sourceWidth + padding * 2, height = sourceHeight + padding * 2;
            var alpha = new float[width * height];
            RenderTexture previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            RenderTexture temporary = null;
            Texture2D readback = null, resultTexture = null;
            Sprite resultSprite = null;
            bool completed = false;
            try
            {
                temporary = RenderTexture.GetTemporary(sourceWidth, sourceHeight, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                GL.sRGBWrite = false;
                Graphics.Blit(sourceTexture, temporary,
                    new Vector2(crop.width / sourceTexture.width, crop.height / sourceTexture.height),
                    new Vector2(crop.x / sourceTexture.width, crop.y / sourceTexture.height));
                RenderTexture.active = temporary;
                readback = new Texture2D(sourceWidth, sourceHeight, TextureFormat.RGBA32, false, true)
                { hideFlags = HideFlags.DontSave };
                readback.ReadPixels(new Rect(0, 0, sourceWidth, sourceHeight), 0, 0, false);
                Color32[] sampled = readback.GetPixels32();
                for (int y = 0; y < sourceHeight; y++)
                for (int x = 0; x < sourceWidth; x++)
                    alpha[(y + padding) * width + x + padding] = sampled[y * sourceWidth + x].a / 255f;

                float largestSide = Mathf.Max(cropWidth, cropHeight);
                float[] tight = Blur(alpha, width, height, Mathf.Max(1, largestSide * .035f));
                float[] soft = Blur(alpha, width, height, Mathf.Max(1, largestSide * .085f));
                var pixels = new Color[alpha.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    // White RGB prevents color fringes from transparent source texels.
                    // The renderer supplies the flower's tint; alpha is applied once
                    // by the premultiplied additive shader.
                    float opacity = Mathf.Clamp01(tight[i] * .72f + soft[i] * .48f);
                    pixels[i] = new Color(1, 1, 1, opacity);
                }
                // Keep the low-intensity skirt smooth against the dark room;
                // quantizing it to 8-bit alpha makes its outer edge visibly band.
                resultTexture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
                {
                    name = "Light flower cached petal bloom",
                    hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                resultTexture.SetPixels(pixels);
                resultTexture.Apply(false, true);
                // A fractional final rect keeps the exact source aspect and world
                // dimensions when a large crop required bounded resampling.
                float spriteWidth = cropWidth + padding * 2, spriteHeight = cropHeight + padding * 2;
                var pivot = new Vector2((source.pivot.x * samplingScale + padding) / spriteWidth,
                    (source.pivot.y * samplingScale + padding) / spriteHeight);
                resultSprite = Sprite.Create(resultTexture, new Rect(0, 0, spriteWidth, spriteHeight),
                    pivot, source.pixelsPerUnit * samplingScale, 0, SpriteMeshType.FullRect);
                resultSprite.name = "Light flower petal bloom";
                resultSprite.hideFlags = HideFlags.DontSave;
                ownedTexture = resultTexture;
                completed = true;
                return resultSprite;
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgbWrite;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
                DestroyOwned(readback);
                if (!completed)
                {
                    DestroyOwned(resultSprite);
                    DestroyOwned(resultTexture);
                }
            }
        }

        static float[] Blur(float[] source, int width, int height, float sigma)
        {
            int radius = Mathf.CeilToInt(sigma * 3);
            var weights = new float[radius * 2 + 1];
            float total = 0;
            for (int offset = -radius; offset <= radius; offset++)
            {
                float value = Mathf.Exp(-offset * offset / (2 * sigma * sigma));
                weights[offset + radius] = value;
                total += value;
            }
            for (int i = 0; i < weights.Length; i++) weights[i] /= total;
            var horizontal = new float[source.Length];
            var result = new float[source.Length];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float value = 0;
                for (int offset = -radius; offset <= radius; offset++)
                {
                    int sample = x + offset;
                    if (sample >= 0 && sample < width)
                        value += source[y * width + sample] * weights[offset + radius];
                }
                horizontal[y * width + x] = value;
            }
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float value = 0;
                for (int offset = -radius; offset <= radius; offset++)
                {
                    int sample = y + offset;
                    if (sample >= 0 && sample < height)
                        value += horizontal[sample * width + x] * weights[offset + radius];
                }
                result[y * width + x] = value;
            }
            return result;
        }

        static void DestroyOwned(Object owned)
        {
            if (owned == null) return;
            if (Application.isPlaying) Object.Destroy(owned); else Object.DestroyImmediate(owned);
        }
    }
}
