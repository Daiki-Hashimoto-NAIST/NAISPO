using UnityEngine;

namespace Naispo.Interaction
{
    public sealed partial class CooperativeCoinGame
    {
        RallyGardenWorld garden;
        RallyCyberWorld cyber;
        SpriteRenderer[] courtArtwork;
        Texture2D buttonNormal,buttonHover,buttonPressed;
        GUIStyle quietButtonStyle,sliderStyle,sliderThumbStyle;
        public RallyGardenWorld GardenPresentation => garden;
        public RallyCyberWorld CyberPresentation => cyber;

        void BuildGardenPresentation()
        {
            // Snapshot only the structural court, before balls, stars and items are added.
            courtArtwork=arenaRoot.GetComponentsInChildren<SpriteRenderer>();
            garden=CreateChild("Garden atmosphere",arenaRoot.transform).AddComponent<RallyGardenWorld>();
            garden.Initialize(halfWidth,ArenaFloor,cameraView,visualConfig);
            cyber=CreateChild("Cyber arena atmosphere",arenaRoot.transform).AddComponent<RallyCyberWorld>();
            cyber.Initialize(halfWidth,MatchFloor,cameraView,visualConfig);
            RefreshPresentation();
        }
        void RefreshPresentation()
        {
            if(garden!=null)garden.SetVisible(!IsVersus);
            if(battleEffects!=null)battleEffects.SetVisible(IsVersus);
            // The decorative net must mirror the physical collider from its true floor
            // (ArenaFloor), including the minimum-height setting where it only spans 1 unit.
            if(cyber!=null) { cyber.SetVisible(IsVersus);cyber.SetNet(ArenaFloor+netHeight,ArenaFloor); }
            if(courtArtwork!=null)for(int i=0;i<courtArtwork.Length;i++)
            {
                var view=courtArtwork[i];
                // The arena supplies its own thin net and edge marks. Keep every
                // existing collider transform intact when hiding the old artwork.
                if(IsVersus) { view.color=Color.clear;continue; }
                string name=view.name;
                if(name.Contains("wash")||name=="Upper boundary") view.color=Color.clear;
                else if(name.Contains("outer wall")) view.color=new Color(.055f,.16f,.15f,.30f);
                else if(name=="Left baseline") view.color=new Color(PlayerColor(-1).r,PlayerColor(-1).g,PlayerColor(-1).b,.52f);
                else if(name=="Right baseline") view.color=new Color(PlayerColor(1).r,PlayerColor(1).g,PlayerColor(1).b,.52f);
                else if(name=="Low center net") view.color=new Color(.52f,.73f,.63f,.38f);
                else if(name=="Net top") view.color=new Color(.94f,.86f,.62f,.95f);
                else view.color=new Color(.65f,.81f,.70f,.50f);
            }
            if(panelTexture!=null)
            {
                SetTextureColor(panelTexture,IsVersus?new Color(.055f,.040f,.095f,.96f):new Color(.014f,.052f,.058f,.97f));
                SetTextureColor(buttonNormal,IsVersus?new Color(.10f,.075f,.15f):new Color(.045f,.13f,.13f));
                SetTextureColor(buttonHover,IsVersus?new Color(.17f,.12f,.24f):new Color(.08f,.21f,.20f));
                SetTextureColor(buttonPressed,IsVersus?new Color(.25f,.18f,.34f):new Color(.13f,.31f,.28f));
                smallStyle.normal.textColor=IsVersus ? new Color(.70f,.66f,.80f) : new Color(.58f,.72f,.68f);
                goalStyle.normal.textColor=IsVersus ? new Color(.82f,.78f,.92f) : new Color(.83f,.87f,.78f);
                leftScoreStyle.normal.textColor=PlayerColor(-1);
                rightScoreStyle.normal.textColor=PlayerColor(1);
                titleStyle.normal.textColor=bodyStyle.normal.textColor=centeredStyle.normal.textColor=
                    IsVersus ? new Color(.92f,.88f,.98f) : new Color(.95f,.94f,.84f);
                buttonStyle.normal.textColor=IsVersus ? new Color(.92f,.88f,.98f) : new Color(.89f,.94f,.87f);
                quietButtonStyle.normal.textColor=IsVersus ? new Color(.68f,.62f,.76f) : new Color(.66f,.76f,.69f);
            }
        }
        static Texture2D SolidTexture(Color color)
        {
            var texture=new Texture2D(1,1,TextureFormat.RGBA32,false);
            SetTextureColor(texture,color);return texture;
        }
        static void SetTextureColor(Texture2D texture,Color color)
        {
            if(texture==null)return;texture.SetPixel(0,0,color);texture.Apply();
        }
        void BuildControlStyles()
        {
            buttonNormal=SolidTexture(Color.white);buttonHover=SolidTexture(Color.white);buttonPressed=SolidTexture(Color.white);
            buttonStyle=new GUIStyle() {
                font=GuiFont,fontSize=16,fontStyle=FontStyle.Normal,alignment=TextAnchor.MiddleCenter,
                border=new RectOffset(),padding=new RectOffset(10,10,6,6),wordWrap=false,
                normal={background=buttonNormal,textColor=new Color(.89f,.94f,.87f)},
                hover={background=buttonHover,textColor=Color.white},
                active={background=buttonPressed,textColor=Color.white},
                focused={background=buttonHover,textColor=Color.white}
            };
            quietButtonStyle=new GUIStyle(buttonStyle) {
                normal={background=null,textColor=new Color(.66f,.76f,.69f)},
                hover={background=buttonHover},active={background=buttonPressed}
            };
            sliderStyle=new GUIStyle() {
                fixedHeight=3,border=new RectOffset(),margin=new RectOffset(0,0,8,8),
                normal={background=buttonHover}
            };
            sliderThumbStyle=new GUIStyle() {
                fixedWidth=12,fixedHeight=16,border=new RectOffset(),margin=new RectOffset(0,0,-6,0),
                normal={background=Texture2D.whiteTexture},
                hover={background=Texture2D.whiteTexture},
                active={background=Texture2D.whiteTexture}
            };
            RefreshPresentation();
        }
        void DrawBottomControls()
        {
            bool painted = IsVersus ? cyber != null && cyber.UsesConfiguredBackground
                : garden != null && garden.UsesConfiguredBackground;
            if(painted)
            {
                // Local backing keeps the controls readable over reflective floors
                // without covering the room with a full-width footer.
                Color backing=IsVersus ? new Color(.035f,.025f,.06f,.80f) : new Color(.012f,.035f,.038f,.80f);
                Fill(new Rect(34,680,166,38),backing);
                Fill(new Rect(1134,680,104,38),backing);
            }
            GUI.enabled=!settingsOpen;
            if(GUI.Button(new Rect(42,682,150,34),IsVersus ? "対戦  ▾" : "星あつめ  ▾",quietButtonStyle))
            { settingsOpen=true;ApplySimulationState(); }
            var pauseRect=new Rect(1136,682,44,34);
            if(GUI.Button(pauseRect,GUIContent.none,quietButtonStyle))SetPaused(!paused);
            Color ink=IsVersus ? new Color(.65f,.75f,.84f) : new Color(.70f,.80f,.72f);
            if(paused)
            {
                Stroke(new Vector2(1153,686),new Vector2(1165,694),2,ink);
                Stroke(new Vector2(1165,694),new Vector2(1153,702),2,ink);
                Stroke(new Vector2(1153,702),new Vector2(1153,686),2,ink);
            }
            else
            {
                Fill(new Rect(1152,686,4,16),ink);Fill(new Rect(1161,686,4,16),ink);
            }
            if(GUI.Button(new Rect(1192,682,44,34),GUIContent.none,quietButtonStyle))
            { settingsOpen=true;ApplySimulationState(); }
            for(int i=0;i<3;i++)
            {
                float y=686+i*7;Stroke(new Vector2(1204,y),new Vector2(1224,y),1.5f,ink);
                float x=i==1?1218:1210;Fill(new Rect(x-2,y-3,4,6),ink);
            }
            GUI.enabled=true;
        }
        void DrawPauseMenu()
        {
            Fill(new Rect(0,0,1280,720),IsVersus ? new Color(.01f,.02f,.04f,.65f) : new Color(.015f,.035f,.025f,.65f));
            GUI.DrawTexture(new Rect(480,236,320,248),panelTexture);
            Color pauseInk=IsVersus ? new Color(.75f,.85f,.94f) : new Color(.75f,.84f,.72f);
            Fill(new Rect(625,265,10,28),pauseInk);
            Fill(new Rect(644,265,10,28),pauseInk);
            if(GUI.Button(new Rect(512,319,256,42),"再開",buttonStyle))SetPaused(false);
            if(GUI.Button(new Rect(512,375,256,38),"はじめから",quietButtonStyle))RestartGame();
            if(GUI.Button(new Rect(512,423,256,38),"設定",quietButtonStyle))
            { settingsOpen=true;ApplySimulationState(); }
        }
        void CleanupGardenPresentation()
        {
            Release(buttonNormal);Release(buttonHover);Release(buttonPressed);
        }
    }
}
