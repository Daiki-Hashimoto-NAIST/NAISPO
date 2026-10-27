using System;
using UnityEngine;

namespace Naispo.Interaction
{
    public enum RallyBackdropDepthProfile { Uniform = 0, Glasshouse = 1, DuelingHall = 2 }

    /// <summary>
    /// Painted scene depths, authored in top-origin coordinates inside the sprite rect.
    /// These shapes follow the supplied architecture; replacement art should use Uniform.
    /// The caller owns the generated linear texture. Source pixels are never read.
    /// </summary>
    public static class RallyBackdropDepth
    {
        public static Texture2D CreateTexture(Sprite sprite, RallyBackdropDepthProfile profile)
        {
            if (profile == RallyBackdropDepthProfile.Uniform || sprite == null || sprite.texture == null)
                return null;
            var map = new DepthCanvas(sprite.texture.width, sprite.texture.height, sprite.rect);
            bool garden = profile == RallyBackdropDepthProfile.Glasshouse;
            if (garden) GardenWalls(map); else HallWalls(map);

            // The floor recedes continuously; column bases are drawn over it afterward.
            map.Box(0, .849f, 1, 1, p => .03f + .77f * Mathf.Pow(
                Mathf.Clamp01((1 - p.y) / .151f), 1.3f));

            for (int side = 0; side < 2; side++)
            {
                bool mirror = side != 0;
                if (!garden)
                    Column(map, mirror, .047f, .07f, .029f, .062f, .04f);
                Column(map, mirror, garden ? .146f : .151f, garden ? .12f : .10f,
                    .031f, .060f, .13f);
                Column(map, mirror, garden ? .253f : .263f, garden ? .355f : .39f,
                    .025f, .050f, .38f);
                Column(map, mirror, garden ? .352f : .351f, .16f, .025f, .051f, .54f);

                // Ribs and masonry arches retain their structural depth from end to end.
                map.Curve(mirror, .146f, .125f, .145f, .075f, .160f, .025f,
                    .180f, -.04f, .034f, .13f);
                map.Curve(mirror, .352f, .175f, .347f, .105f, .373f, .025f,
                    .399f, -.04f, .021f, .54f);
                map.Curve(mirror, .055f, .142f, .17f, .17f, .28f, .20f,
                    .351f, .20f, .018f, .16f, .54f);
                map.Curve(mirror, .351f, .20f, .40f, .21f, .45f, .212f,
                    .50f, .209f, .018f, .54f, .75f);
                map.Curve(mirror, .253f, .365f, .253f, .235f, .207f, .194f,
                    .164f, .207f, .018f, .38f);
                map.Curve(mirror, .351f, .32f, .32f, .20f, .277f, .21f,
                    .263f, .39f, .014f, .54f);
                map.Curve(mirror, .351f, .30f, .372f, .205f, .431f, .205f,
                    .445f, .315f, .010f, .65f);
            }

            // The fine back roof and its glazing sit behind the larger roof ribs.
            map.Curve(false, .445f, .315f, .452f, .205f, .548f, .205f,
                .555f, .315f, .009f, .82f);
            map.Line(.50f, 0, .50f, .209f, .003f, .75f);
            map.Line(.399f, 0, .445f, .315f, .003f, .75f);
            map.Line(.601f, 0, .555f, .315f, .003f, .75f);
            return map.CreateTexture(garden ? "Glasshouse scene depth" : "Dueling hall scene depth");
        }

        static void Column(DepthCanvas map, bool mirror, float center, float top,
            float shaftWidth, float capitalWidth, float depth)
        {
            if (mirror) center = 1 - center;
            // A constant depth covers the complete shaft, capital, collars and plinth.
            map.Box(center - shaftWidth / 2, top, center + shaftWidth / 2, .849f, depth);
            map.Box(center - capitalWidth / 2, top, center + capitalWidth / 2, top + .035f, depth);
            map.Box(center - shaftWidth * .67f, top + .035f,
                center + shaftWidth * .67f, top + .067f, depth);
            map.Box(center - capitalWidth * .43f, .72f, center + capitalWidth * .43f, .849f, depth);
            map.Box(center - capitalWidth / 2, .812f, center + capitalWidth / 2, .849f, depth);
            float collar = top < .2f ? .665f : .715f;
            map.Box(center - shaftWidth * .72f, collar, center + shaftWidth * .72f, collar + .021f, depth);
        }

        static void GardenWalls(DepthCanvas map)
        {
            // Near bookcases, the two receding planting beds, and the far doorway.
            map.Polygon(new[] { V(0,.16f), V(.125f,.225f), V(.125f,.85f), V(0,.87f) }, p => .10f);
            map.Polygon(new[] { V(1,.16f), V(.875f,.225f), V(.875f,.85f), V(1,.87f) }, p => .10f);
            map.Polygon(new[] { V(.24f,.76f), V(.38f,.77f), V(.38f,.849f), V(.24f,.849f) }, p => .57f);
            map.Polygon(new[] { V(.76f,.76f), V(.62f,.77f), V(.62f,.849f), V(.76f,.849f) }, p => .57f);
            map.Box(.443f, .50f, .457f, .849f, .90f);
            map.Box(.543f, .50f, .557f, .849f, .90f);
            map.Curve(false, .45f,.51f, .45f,.405f, .55f,.405f, .55f,.51f, .016f,.90f);
            for (int side = 0; side < 2; side++)
            {
                bool mirror = side != 0;
                // Irregular silhouettes keep foreground leaves with their nearby frames.
                // The smaller hanging leaves are covered by these clusters, not a field-wide veil.
                map.Polygon(new[] { M(0,0,mirror), M(.264f,0,mirror), M(.248f,.035f,mirror),
                    M(.217f,.060f,mirror), M(.208f,.105f,mirror), M(.179f,.113f,mirror),
                    M(.174f,.150f,mirror), M(.134f,.128f,mirror), M(.111f,.139f,mirror),
                    M(.085f,.180f,mirror), M(.032f,.175f,mirror), M(0,.220f,mirror) }, p=>.13f);
                map.Polygon(new[] { M(.160f,.110f,mirror), M(.205f,.140f,mirror),
                    M(.208f,.230f,mirror), M(.196f,.320f,mirror), M(.210f,.390f,mirror),
                    M(.194f,.480f,mirror), M(.178f,.600f,mirror), M(.185f,.720f,mirror),
                    M(.160f,.849f,mirror), M(.148f,.840f,mirror), M(.157f,.630f,mirror),
                    M(.155f,.430f,mirror), M(.166f,.270f,mirror) }, p=>.13f);
                map.Polygon(new[] { M(.170f,.849f,mirror), M(.164f,.760f,mirror),
                    M(.173f,.640f,mirror), M(.168f,.560f,mirror), M(.185f,.475f,mirror),
                    M(.213f,.435f,mirror), M(.226f,.454f,mirror), M(.207f,.502f,mirror),
                    M(.252f,.498f,mirror), M(.279f,.558f,mirror), M(.258f,.576f,mirror),
                    M(.244f,.558f,mirror), M(.264f,.639f,mirror), M(.251f,.652f,mirror),
                    M(.276f,.696f,mirror), M(.287f,.776f,mirror), M(.301f,.840f,mirror) }, p=>.22f);
            }
        }

        static void HallWalls(DepthCanvas map)
        {
            map.Box(.345f, .325f, .655f, .849f, .88f);
            for (int side = 0; side < 2; side++)
            {
                bool mirror = side != 0;
                map.Polygon(new[] { M(.025f,.14f,mirror), M(.345f,.325f,mirror),
                    M(.345f,.849f,mirror), M(.025f,.849f,mirror) }, p =>
                    Mathf.Lerp(.20f, .86f, Mathf.InverseLerp(.025f,.345f,mirror ? 1-p.x : p.x)));

                // Window voids reveal distant trees and sky through the nearer masonry.
                map.Window(mirror, .074f, .126f, .277f, .35f, .74f);
                map.Window(mirror, .181f, .242f, .286f, .40f, .74f);
                map.Window(mirror, .282f, .331f, .402f, .48f, .758f);
                map.Window(mirror, .369f, .428f, .449f, .51f, .758f);
                map.Curve(mirror, .176f,.40f, .175f,.19f, .244f,.19f, .251f,.40f, .020f,.32f);
                map.Curve(mirror, .282f,.45f, .286f,.30f, .333f,.30f, .341f,.45f, .018f,.53f);
                map.Curve(mirror, .366f,.49f, .378f,.34f, .429f,.34f, .438f,.49f, .018f,.80f);
                float x = mirror ? .555f : .445f;
                map.Box(x-.007f,.315f,x+.007f,.849f,.84f);
            }
            map.Window(false, .465f, .533f, .457f, .51f, .763f);
            map.Curve(false, .448f,.49f, .459f,.35f, .542f,.35f, .553f,.49f, .018f,.88f);
        }

        static Vector2 V(float x, float y) => new Vector2(x,y);
        static Vector2 M(float x, float y, bool mirror) => V(mirror ? 1-x : x,y);

        sealed class DepthCanvas
        {
            readonly int width, height;
            readonly Rect crop;
            readonly Color32[] pixels;
            readonly float featherPixels;

            public DepthCanvas(int width, int height, Rect crop)
            {
                this.width = width;
                this.height = height;
                this.crop = crop;
                // These are traced scene layers rather than exact object mattes.
                // A small image-relative transition avoids visible tracing edges.
                featherPixels = Mathf.Max(1, crop.width * .004f);
                pixels = new Color32[width * height];
                var far = new Color32(255,255,255,255);
                for (int i=0; i<pixels.Length; i++) pixels[i] = far;
            }

            Vector2 Pixel(Vector2 uv) => V(uv.x * crop.width, uv.y * crop.height);

            public void Box(float left, float top, float right, float bottom, float depth)
                => Box(left,top,right,bottom,p => depth);

            public void Box(float left, float top, float right, float bottom, Func<Vector2,float> depth,
                bool replace = false)
            {
                Vector2 a = Pixel(V(left,top)), b = Pixel(V(right,bottom));
                Stamp(Rect.MinMaxRect(a.x,a.y,b.x,b.y), p =>
                    Mathf.Min(Mathf.Min(p.x-a.x,b.x-p.x),Mathf.Min(p.y-a.y,b.y-p.y)), depth, replace);
            }

            public void Window(bool mirror, float left, float right, float top, float spring, float bottom)
            {
                if (mirror) { float oldLeft=left; left=1-right; right=1-oldLeft; }
                Vector2 center=Pixel(V((left+right)/2,spring));
                Vector2 radius=Pixel(V((right-left)/2,spring-top));
                Vector2 baseCorner=Pixel(V(right,bottom));
                // Union the full ellipse and rectangular shaft before feathering.
                // Their overlap removes an artificial edge across the arch's spring line.
                Stamp(Rect.MinMaxRect(center.x-radius.x,center.y-radius.y,center.x+radius.x,baseCorner.y), p =>
                {
                    Vector2 delta=p-center;
                    float ellipse=1-Mathf.Sqrt(delta.x*delta.x/(radius.x*radius.x)+delta.y*delta.y/(radius.y*radius.y));
                    float shaft=Mathf.Min(Mathf.Min(p.x-(center.x-radius.x),baseCorner.x-p.x),
                        Mathf.Min(p.y-center.y,baseCorner.y-p.y));
                    return Mathf.Max(ellipse*Mathf.Min(radius.x,radius.y),shaft);
                }, p=>1,true);
            }

            public void Polygon(Vector2[] points, Func<Vector2,float> depth)
            {
                Vector2 min=V(float.PositiveInfinity,float.PositiveInfinity), max=-min;
                for(int i=0;i<points.Length;i++)
                {
                    points[i]=Pixel(points[i]); min=Vector2.Min(min,points[i]); max=Vector2.Max(max,points[i]);
                }
                Stamp(Rect.MinMaxRect(min.x,min.y,max.x,max.y), p =>
                {
                    bool inside=false; float distance=float.PositiveInfinity;
                    for(int i=0,j=points.Length-1;i<points.Length;j=i++)
                    {
                        Vector2 a=points[j], b=points[i];
                        if ((a.y>p.y)!=(b.y>p.y) && p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x) inside=!inside;
                        distance=Mathf.Min(distance,SegmentDistance(p,a,b));
                    }
                    return inside ? distance : -distance;
                },depth,false);
            }

            public void Curve(bool mirror, float ax,float ay,float bx,float by,float cx,float cy,
                float dx,float dy,float thickness,float depth,float endDepth=-1)
            {
                Vector2 a=M(ax,ay,mirror), b=M(bx,by,mirror), c=M(cx,cy,mirror), d=M(dx,dy,mirror), previous=a;
                const int segments=24;
                for(int i=1;i<=segments;i++)
                {
                    float t=i/(float)segments, q=1-t;
                    Vector2 next=q*q*q*a+3*q*q*t*b+3*q*t*t*c+t*t*t*d;
                    Line(previous.x,previous.y,next.x,next.y,thickness,
                        endDepth<0 ? depth : Mathf.Lerp(depth,endDepth,(i-.5f)/segments));
                    previous=next;
                }
            }

            public void Line(float ax,float ay,float bx,float by,float thickness,float depth)
            {
                Vector2 a=Pixel(V(ax,ay)), b=Pixel(V(bx,by));
                float radius=thickness*crop.width/2;
                Vector2 min=Vector2.Min(a,b)-Vector2.one*radius, max=Vector2.Max(a,b)+Vector2.one*radius;
                Stamp(Rect.MinMaxRect(min.x,min.y,max.x,max.y),p=>radius-SegmentDistance(p,a,b),p=>depth,false);
            }

            static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
            {
                Vector2 ab=b-a;
                return Vector2.Distance(p,a+ab*Mathf.Clamp01(Vector2.Dot(p-a,ab)/Mathf.Max(ab.sqrMagnitude,.0001f)));
            }

            void Stamp(Rect bounds, Func<Vector2,float> signedDistance, Func<Vector2,float> depth, bool replace)
            {
                int x0=Mathf.Max(0,Mathf.FloorToInt(bounds.xMin-featherPixels));
                int x1=Mathf.Min(Mathf.CeilToInt(crop.width),Mathf.CeilToInt(bounds.xMax+featherPixels));
                int y0=Mathf.Max(0,Mathf.FloorToInt(bounds.yMin-featherPixels));
                int y1=Mathf.Min(Mathf.CeilToInt(crop.height),Mathf.CeilToInt(bounds.yMax+featherPixels));
                for(int y=y0;y<y1;y++) for(int x=x0;x<x1;x++)
                {
                    Vector2 point=V(x+.5f,y+.5f);
                    float coverage=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-featherPixels,featherPixels,signedDistance(point)));
                    if(coverage<=0) continue;
                    int px=Mathf.FloorToInt(crop.xMin)+x;
                    int py=Mathf.CeilToInt(crop.yMax)-1-y; // Convert authored top-origin once.
                    if(px<0 || px>=width || py<0 || py>=height) continue;
                    int index=py*width+px;
                    float current=pixels[index].r/255f;
                    float target=Mathf.Clamp01(depth(V(point.x/crop.width,point.y/crop.height)));
                    if(!replace) target=Mathf.Min(current,target);
                    byte value=(byte)Mathf.RoundToInt(Mathf.Lerp(current,target,coverage)*255);
                    pixels[index]=new Color32(value,value,value,255);
                }
            }

            public Texture2D CreateTexture(string name)
            {
                var texture=new Texture2D(width,height,TextureFormat.RGBA32,false,true)
                {
                    name=name, hideFlags=HideFlags.DontSave,
                    wrapMode=TextureWrapMode.Clamp, filterMode=FilterMode.Bilinear
                };
                texture.SetPixels32(pixels);
                texture.Apply(false,false);
                return texture;
            }
        }
    }
}
