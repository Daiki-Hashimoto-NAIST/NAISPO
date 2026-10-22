using UnityEngine;
using Naispo.Kinect;

namespace Naispo.Silhouette
{
    public sealed class KinectViewportMapper : MonoBehaviour
    {
        public Camera targetCamera;
        public KinectSettings settings;
        public float silhouettePlaneZ;
        [Tooltip("Normalized portion of the common camera world; does not split physics.")]
        public Rect viewport = new Rect(0,0,1,1);
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
            center += new Vector2((viewport.center.x-.5f)*size.x,(viewport.center.y-.5f)*size.y);
            size = Vector2.Scale(size,viewport.size);
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

        // Calibration uses pixel centers (first center = 0,0), while the shared
        // image/contour mapper uses pixel edges (first center = 0.5,0.5).
        public Vector2 DepthPixelCenterToWorld(Vector2 pixel) => PixelToWorld(pixel + Vector2.one * 0.5f);
        public bool ContainsDepthPixelCenter(Vector2 pixel) => pixel.x >= -0.5f && pixel.y >= -0.5f && pixel.x < Width - 0.5f && pixel.y < Height - 0.5f;
    }
}
