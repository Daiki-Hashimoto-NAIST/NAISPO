using System.IO;
using UnityEditor;
using UnityEngine;

public static class BallLightPreview
{
    // Synthetic calibration-style preview; no camera recording.
    public static void Render()
    {
        var shader=Shader.Find("Naispo/BallLightReveal");
        if(shader==null || !shader.isSupported) throw new System.Exception("Reveal shader unavailable");
        var material=new Material(shader);
        var cameraImage=new Texture2D(128,128,TextureFormat.RGBA32,false);
        var mask=new Texture2D(128,128,TextureFormat.RGBA32,false);
        for(int y=0;y<128;y++)for(int x=0;x<128;x++)
        {
            cameraImage.SetPixel(x,y,new Color(x/127f,y/127f,0.3f,1));
            bool body=x>34&&x<94&&y>20&&y<115;
            mask.SetPixel(x,y,body?Color.cyan:Color.clear);
        }
        cameraImage.Apply();mask.Apply();material.mainTexture=mask;material.SetTexture("_ColorTex",cameraImage);
        material.SetFloat("_HasColor",1);material.SetFloat("_Radius",0.26f);material.SetFloat("_ShadowBrightness",0.14f);
        material.SetVector("_LightPosition",new Vector4(0.5f,0.5f,0,0));
        var mesh=new Mesh();mesh.vertices=new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0)};
        mesh.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};
        mesh.triangles=new[]{0,1,2,0,2,3};
        var rt=new RenderTexture(512,512,0,RenderTextureFormat.ARGB32);rt.Create();
        var previous=RenderTexture.active;RenderTexture.active=rt;GL.Clear(true,true,Color.black);
        GL.PushMatrix();GL.LoadOrtho();material.SetPass(0);Graphics.DrawMeshNow(mesh,Matrix4x4.identity);GL.PopMatrix();
        var output=new Texture2D(512,512,TextureFormat.RGB24,false);output.ReadPixels(new Rect(0,0,512,512),0,0);output.Apply();
        Directory.CreateDirectory("Logs");File.WriteAllBytes("Logs/ball-light-preview.png",output.EncodeToPNG());
        var center=output.GetPixel(256,256);var corner=output.GetPixel(10,10);var shadow=output.GetPixel(256,450);
        Debug.Log($"BALL_LIGHT_PREVIEW center={center} corner={corner} shadow={shadow}");
        if(center.maxColorComponent<0.3f || corner.maxColorComponent>0.12f || shadow.maxColorComponent>=center.maxColorComponent)
            throw new System.Exception("Reveal shader brightness checks failed");
        RenderTexture.active=previous;
        Object.DestroyImmediate(output);Object.DestroyImmediate(rt);Object.DestroyImmediate(mesh);
        Object.DestroyImmediate(cameraImage);Object.DestroyImmediate(mask);Object.DestroyImmediate(material);
    }
}
