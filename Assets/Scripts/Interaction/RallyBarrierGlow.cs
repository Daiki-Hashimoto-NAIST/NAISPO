using UnityEngine;
using Naispo.Silhouette;

namespace Naispo.Interaction
{
    // Decorative layers follow the physical barrier's actual deformed samples.
    [DefaultExecutionOrder(200)]
    public sealed class RallyBarrierGlow : MonoBehaviour
    {
        LineRenderer source, outer, inner;
        Material material;
        Sprite orbSprite;
        Texture2D orbTexture;
        readonly SpriteRenderer[] ends=new SpriteRenderer[2];
        readonly MaterialPropertyBlock[] endProperties=new MaterialPropertyBlock[2];
        bool visible;

        public void Configure(LineRenderer line,bool garden)
        {
            source=line;visible=true;
            if(material!=null) { ApplyTheme(garden);return; }
            // Reuse the scene-referenced silhouette shader so the world-space
            // mask is included in player builds and shared by every glow layer.
            material=new Material(line.sharedMaterial.shader);
            material.mainTexture=Texture2D.whiteTexture;
            material.SetFloat("_UseVertexColor",1);
            outer=Layer("Soft ribbon halo",.25f,8);
            inner=Layer("Ribbon light",.16f,9);
            orbTexture=new Texture2D(64,64,TextureFormat.RGBA32,false) { wrapMode=TextureWrapMode.Clamp };
            for(int y=0;y<64;y++)for(int x=0;x<64;x++)
            {
                float distance=Vector2.Distance(new Vector2(x+.5f,y+.5f),Vector2.one*32)/32;
                orbTexture.SetPixel(x,y,new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-distance),2)));
            }
            orbTexture.Apply();
            orbSprite=Sprite.Create(orbTexture,new Rect(0,0,64,64),Vector2.one*.5f,64);
            for(int i=0;i<2;i++)
            {
                var child=new GameObject("Hand light "+i);child.transform.SetParent(transform,false);
                ends[i]=child.AddComponent<SpriteRenderer>();ends[i].sprite=orbSprite;
                ends[i].sharedMaterial=material;
                endProperties[i]=new MaterialPropertyBlock();
                endProperties[i].SetTexture("_MainTex",orbTexture);
                endProperties[i].SetFloat("_UseVertexColor",0);
                ends[i].sortingOrder=11;child.transform.localScale=Vector3.one*.42f;
            }
            ApplyTheme(garden);
        }
        public void ConfigureCourt(float centerX,float halfWidth,int side)
        {
            CourtEdgeFade.Configure(material,centerX,halfWidth,side);
        }
        void ApplyTheme(bool garden)
        {
            // Thin luminous threads follow the exact deformed source samples. The
            // physical collider remains the source EdgeCollider2D and is never widened.
            outer.startWidth=outer.endWidth=garden?.12f:.10f;
            inner.startWidth=inner.endWidth=garden?.065f:.055f;
            for(int i=0;i<ends.Length;i++)ends[i].transform.localScale=Vector3.one*(garden?.14f:.11f);
        }
        LineRenderer Layer(string name,float width,int order)
        {
            var child=new GameObject(name);child.transform.SetParent(transform,false);
            var line=child.AddComponent<LineRenderer>();line.sharedMaterial=material;
            line.useWorldSpace=false;line.startWidth=line.endWidth=width;
            line.numCapVertices=8;line.numCornerVertices=4;line.sortingOrder=order;
            return line;
        }
        void LateUpdate()
        {
            if(source==null||outer==null)return;
            bool show=visible&&source.enabled&&source.gameObject.activeInHierarchy;
            outer.enabled=inner.enabled=show;
            for(int i=0;i<2;i++)ends[i].enabled=show;
            if(!show)return;
            Color color=source.sharedMaterial.mainTexture is Texture2D texture ? texture.GetPixel(0,0) : Color.white;
            outer.startColor=outer.endColor=new Color(color.r,color.g,color.b,.055f);
            inner.startColor=inner.endColor=new Color(color.r,color.g,color.b,.34f);
            outer.positionCount=inner.positionCount=source.positionCount;
            for(int i=0;i<source.positionCount;i++)
            {
                Vector3 point=source.GetPosition(i);outer.SetPosition(i,point);inner.SetPosition(i,point);
            }
            for(int i=0;i<2;i++)
            {
                ends[i].transform.localPosition=source.GetPosition(i==0?0:source.positionCount-1);
                // Explicit per-sprite tint avoids depending on the render pipeline's
                // sprite instancing color path; line colors still use vertex tint.
                ends[i].color=Color.white;
                endProperties[i].SetColor("_TintColor",new Color(color.r,color.g,color.b,.72f));
                ends[i].SetPropertyBlock(endProperties[i]);
            }
        }
        void OnDestroy()
        {
            Release(material);Release(orbSprite);Release(orbTexture);
        }
        static void Release(Object value)
        {
            if(value==null)return;
            if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);
        }
    }
}
