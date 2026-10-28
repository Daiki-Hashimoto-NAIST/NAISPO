using UnityEngine;

namespace Naispo.Silhouette
{
    /// <summary>
    /// One world-space fade rule shared by the silhouette, hand barrier and glow layers.
    /// The home court stays fully opaque; only the overflow beyond the centre (and, when
    /// configured, beyond the outer court edge) fades. This is presentation only and
    /// never admits or removes collision geometry.
    /// </summary>
    public static class CourtEdgeFade
    {
        public const float Width = .45f;
        static readonly int CenterId=Shader.PropertyToID("_FadeCenterX");
        static readonly int HalfWidthId=Shader.PropertyToID("_FadeHalfWidth");
        static readonly int SideId=Shader.PropertyToID("_FadeSide");
        static readonly int WidthId=Shader.PropertyToID("_FadeWidth");

        public static float Alpha(float worldX,float centerX,float halfWidth,int side)
        {
            if(side==0) return 1f;
            float inward=side*(worldX-centerX);
            float overflow=inward>=0f ? Mathf.Max(0f,inward-halfWidth) : -inward;
            return 1f-Mathf.SmoothStep(0f,1f,overflow/Width);
        }

        public static void Configure(Material material,float centerX,float halfWidth,int side)
        {
            if(material==null || !material.HasProperty(WidthId)) return;
            material.SetFloat(CenterId,centerX);
            material.SetFloat(HalfWidthId,Mathf.Max(0f,halfWidth));
            material.SetFloat(SideId,side);
            material.SetFloat(WidthId,side==0 ? 0f : Width);
        }
    }
}
