using UnityEngine;
using Naispo.Kinect;

namespace Naispo.Silhouette
{
    public sealed class KinectViewportMapper : MonoBehaviour
    {
        public Camera targetCamera;
        public KinectSettings settings;
        public float silhouettePlaneZ;
        public int Width { get; private set; } = 320;
        public int Height { get; private set; } = 288;
        public float UnitsPerPixel { get; private set; }
        public Vector2 Center { get; private set; }
        public Vector2 ViewSize { get; private set; }
        public int Revision { get; private set; }
        bool mirrored;

        public bool Refresh(int width, int height)
        {
            if (targetCamera == null || !targetCamera.orthographic || width <= 0 || height <= 0) return false;
            var size = new Vector2(2 * targetCamera.orthographicSize * targetCamera.aspect, 2 * targetCamera.orthographicSize);
            var center = (Vector2)targetCamera.transform.position;
            bool mirror = settings != null && settings.mirrorX;
            float scale = Mathf.Min(size.x / width, size.y / height);
            if (Width == width && Height == height && ViewSize == size && Center == center && mirrored == mirror && Mathf.Approximately(UnitsPerPixel, scale)) return false;
            Width = width; Height = height; ViewSize = size; Center = center; mirrored = mirror; UnitsPerPixel = scale;
            Revision++;
            return true;
        }

        // Input is the depth image's TOP-LEFT pixel-edge coordinate. Pixel centers
        // are x+0.5/y+0.5; contour boundaries use integral pixel edges.
        public Vector2 PixelToWorld(Vector2 pixel)
        {
            float x = mirrored ? Width - pixel.x : pixel.x;
            return Center + new Vector2((x - Width * 0.5f) * UnitsPerPixel, (Height * 0.5f - pixel.y) * UnitsPerPixel);
        }

        public Vector2 WorldToPixel(Vector2 world)
        {
            Vector2 p = (world - Center) / UnitsPerPixel;
            float x = p.x + Width * 0.5f;
            return new Vector2(mirrored ? Width - x : x, Height * 0.5f - p.y);
        }
    }
}
