using UnityEngine;

namespace Naispo.Interaction
{
    [ExecuteAlways]
    public sealed class ScreenBoundaryController : MonoBehaviour
    {
        public Camera targetCamera;
        public BoxCollider2D topWall,bottomWall,leftWall,rightWall;
        public PhysicsMaterial2D physicsMaterial;
        [Min(0.1f)] public float wallThickness=0.4f;
        [Min(0)] public float visibleInset=0.06f;
        Vector2 lastSize,lastCenter;
        float lastThickness=-1,lastInset=-1;
        void Update()=>Refresh();
        public void Refresh()
        {
            if(targetCamera==null||!targetCamera.orthographic||topWall==null||bottomWall==null||leftWall==null||rightWall==null)return;
            var size=new Vector2(targetCamera.orthographicSize*2*targetCamera.aspect,targetCamera.orthographicSize*2);
            var center=(Vector2)targetCamera.transform.position;
            if(size==lastSize&&center==lastCenter&&wallThickness==lastThickness&&visibleInset==lastInset)return;
            lastSize=size;lastCenter=center;lastThickness=wallThickness;lastInset=visibleInset;
            float t=wallThickness,h=size.y*0.5f,w=size.x*0.5f,inset=Mathf.Min(visibleInset,t);
            SetWall(topWall,center+new Vector2(0,h+t/2-inset),new Vector2(size.x+2*t,t));
            SetWall(bottomWall,center+new Vector2(0,-h-t/2+inset),new Vector2(size.x+2*t,t));
            SetWall(leftWall,center+new Vector2(-w-t/2+inset,0),new Vector2(t,size.y+2*t));
            SetWall(rightWall,center+new Vector2(w+t/2-inset,0),new Vector2(t,size.y+2*t));
        }
        void SetWall(BoxCollider2D wall,Vector2 center,Vector2 size)
        {
            wall.transform.position=new Vector3(center.x,center.y,0);
            wall.transform.localScale=Vector3.one;
            wall.size=size;wall.sharedMaterial=physicsMaterial;
            var sprite=wall.GetComponent<SpriteRenderer>();
            if(sprite!=null){sprite.drawMode=SpriteDrawMode.Sliced;sprite.size=size;}
        }
    }
}
