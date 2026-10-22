using UnityEngine;

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
        bool visible;

        public void Configure(LineRenderer line,bool garden)
        {
            source=line; visible=garden;
            if(material!=null)return;
            material=new Material(Shader.Find("Sprites/Default"));
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
                ends[i].sortingOrder=11;child.transform.localScale=Vector3.one*.42f;
            }
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
            outer.startColor=outer.endColor=new Color(color.r,color.g,color.b,.08f);
            inner.startColor=inner.endColor=new Color(color.r,color.g,color.b,.14f);
            outer.positionCount=inner.positionCount=source.positionCount;
            for(int i=0;i<source.positionCount;i++)
            {
                Vector3 point=source.GetPosition(i);outer.SetPosition(i,point);inner.SetPosition(i,point);
            }
            for(int i=0;i<2;i++)
            {
                ends[i].transform.localPosition=source.GetPosition(i==0?0:source.positionCount-1);
                ends[i].color=new Color(color.r,color.g,color.b,.72f);
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
