using UnityEngine;

namespace Naispo.Interaction
{
    public static class RallyStarArtwork
    {
        // The caller owns the returned texture and must destroy it when finished.
        public static Texture2D Create(int size=96)
        {
            if(size<8) throw new System.ArgumentOutOfRangeException(nameof(size));

            var points=new Vector2[10];
            Vector2 center=Vector2.one*(size*.5f);
            float outerRadius=size*.44f;
            for(int i=0;i<points.Length;i++)
            {
                float angle=Mathf.PI*.5f+i*Mathf.PI/5;
                float radius=outerRadius*(i%2==0 ? 1 : .47f);
                points[i]=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
            }

            const int samples=4;
            var pixels=new Color32[size*size];
            Color lower=new Color(1,.70f,.27f);
            Color upper=new Color(1,.94f,.66f);
            for(int y=0;y<size;y++) for(int x=0;x<size;x++)
            {
                int covered=0;
                for(int sy=0;sy<samples;sy++) for(int sx=0;sx<samples;sx++)
                {
                    var point=new Vector2(x+(sx+.5f)/samples,y+(sy+.5f)/samples);
                    if(Contains(points,point)) covered++;
                }
                float height=Mathf.InverseLerp(center.y-outerRadius,center.y+outerRadius,y+.5f);
                Color color=Color.Lerp(lower,upper,height);
                color.a=covered/(float)(samples*samples);
                pixels[y*size+x]=color;
            }

            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false)
            {
                name="Rally star",
                wrapMode=TextureWrapMode.Clamp,
                filterMode=FilterMode.Bilinear
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        static bool Contains(Vector2[] polygon,Vector2 point)
        {
            bool inside=false;
            for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
            {
                Vector2 a=polygon[i],b=polygon[j];
                if((a.y>point.y)!=(b.y>point.y) &&
                   point.x<(b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x)
                    inside=!inside;
            }
            return inside;
        }
    }
}
