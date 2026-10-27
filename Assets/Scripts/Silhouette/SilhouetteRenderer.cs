using UnityEngine;
using Naispo.Kinect;
using Unity.Profiling;

namespace Naispo.Silhouette
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SilhouetteRenderer : MonoBehaviour
    {
        public KinectSettings settings;
        public KinectViewportMapper mapper;
        public Material silhouetteMaterial;
        public Texture2D Texture => texture;
        Texture2D texture;
        Texture2D colorTexture;
        Transform lightBall;
        public bool LightEnabled = true;
        Mesh mesh;
        Material material;
        Color32[] pixels;
        readonly Color32[] indexColors = new Color32[256];
        readonly Vector3[] vertices = new Vector3[4];
        int mapRevision = -1;
        MeshRenderer view;
        Color outlineColor;
        float outlineWidth;
        bool outlineConfigured;
        static readonly int OutlineColorId=Shader.PropertyToID("_OutlineColor");
        static readonly int OutlineWidthId=Shader.PropertyToID("_OutlineWidth");
        static readonly ProfilerMarker UploadMarker=new ProfilerMarker("Naispo.Silhouette.TextureUpload");

        void Awake()
        {
            mesh = new Mesh { name = "Kinect aspect-fit quad" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) };
            mesh.triangles = new[] { 0,1,2,0,2,3 };
            GetComponent<MeshFilter>().sharedMesh = mesh;
            material = new Material(silhouetteMaterial);
            if(settings != null && settings.ballLightShader != null) material.shader = settings.ballLightShader;
            ApplyOutline();
            view = GetComponent<MeshRenderer>();
            view.sharedMaterial = material;
            view.sortingOrder = 0;
            view.enabled = false;
        }

        // Opt-in presentation on this renderer's material, shared by demo and camera masks.
        // The contour stays inside the mask and never changes its alpha or collision shape.
        public void SetOutline(Color color,float screenPixels)
        {
            outlineColor=color;
            outlineWidth=Mathf.Clamp(screenPixels,0,4);
            outlineConfigured=true;
            ApplyOutline();
        }

        void ApplyOutline()
        {
            if(!outlineConfigured || material==null || !material.HasProperty(OutlineWidthId))return;
            material.SetColor(OutlineColorId,outlineColor);
            material.SetFloat(OutlineWidthId,outlineWidth);
        }

        void Start()
        {
            var ball = FindFirstObjectByType<Naispo.Interaction.AirHockeyBall>();
            if(ball != null) lightBall = ball.transform;
            if(mapper.targetCamera != null) mapper.targetCamera.backgroundColor = new Color(0.004f,0.006f,0.012f);
        }
        void LateUpdate()
        {
            if(material == null || settings.ballLightShader == null) return;
            material.SetFloat("_LightEnabled",LightEnabled && lightBall != null ? 1 : 0);
            material.SetFloat("_Radius",settings.ballLightRadius);
            material.SetFloat("_ShadowBrightness",settings.shadowBrightness);
            if(lightBall != null) material.SetVector("_LightPosition",lightBall.position);
        }

        public void Apply(BodyFrameData frame, BodyColorRegistry registry)
        {
            if(!isActiveAndEnabled)return;
            using var sample=UploadMarker.Auto();
            if (texture == null || texture.width != frame.Width || texture.height != frame.Height)
            {
                if (texture != null) Destroy(texture);
                texture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false) { name = "Body ID Silhouettes", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
                pixels = new Color32[frame.Width * frame.Height];
                material.mainTexture = texture;
                if(colorTexture != null) Destroy(colorTexture);
                colorTexture = new Texture2D(frame.Width,frame.Height,TextureFormat.BGRA32,false) { name="Depth-aligned RGB",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear };
                if(settings.ballLightShader != null) material.SetTexture("_ColorTex",colorTexture);
            }
            for (int i = 0; i < indexColors.Length; i++)
            {
                int slot = registry.BodyIndexToSlot[i];
                indexColors[i] = slot >= 0 && settings.personColors != null && slot < settings.personColors.Length
                    ? (Color32)settings.personColors[slot] : new Color32(0,0,0,0);
            }
            for (int i = 0; i < pixels.Length; i++) pixels[i] = indexColors[frame.BodyIndexMap[i]];
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            if(frame.HasColor)
            {
                colorTexture.LoadRawTextureData(frame.AlignedColorBgra);
                colorTexture.Apply(false,false);
            }
            if(settings.ballLightShader != null) material.SetFloat("_HasColor",frame.HasColor ? 1 : 0);
            view.enabled = true;
            RefreshGeometry();
        }

        public void RefreshGeometry()
        {
            if (mesh == null || mapper.Revision == mapRevision) return;
            mapRevision = mapper.Revision;
            SetVertex(0, 0, 0); SetVertex(1, mapper.Width, 0);
            SetVertex(2, mapper.Width, mapper.Height); SetVertex(3, 0, mapper.Height);
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }
        void SetVertex(int i, float x, float y)
        {
            Vector2 world = mapper.PixelToWorld(new Vector2(x,y));
            vertices[i] = transform.InverseTransformPoint(new Vector3(world.x, world.y, mapper.silhouettePlaneZ));
        }
        public void Clear() { if (view != null) view.enabled = false; if(material != null && settings.ballLightShader != null)material.SetFloat("_HasColor",0); }
        void OnDisable() => Clear();
        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
            if (texture != null) Destroy(texture);
            if (colorTexture != null) Destroy(colorTexture);
        }
    }
}
