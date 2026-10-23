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

        void BuildGardenPresentation()
        {
            // Snapshot only the structural court, before balls, stars and items are added.
            courtArtwork=arenaRoot.GetComponentsInChildren<SpriteRenderer>();
            garden=CreateChild("Garden atmosphere",arenaRoot.transform).AddComponent<RallyGardenWorld>();
            garden.Initialize(halfWidth,ArenaFloor,cameraView);
            cyber=CreateChild("Cyber arena atmosphere",arenaRoot.transform).AddComponent<RallyCyberWorld>();
            cyber.Initialize(halfWidth,MatchFloor,cameraView);
            RefreshPresentation();
        }
        void RefreshPresentation()
        {
            if(garden!=null)garden.SetVisible(!IsVersus);
            if(battleEffects!=null)battleEffects.SetVisible(IsVersus);
            if(cyber!=null) { cyber.SetVisible(IsVersus);cyber.SetNet(ArenaFloor+netHeight,MatchFloor); }
            if(courtArtwork!=null)for(int i=0;i<courtArtwork.Length;i++)
            {
                var view=courtArtwork[i];
                // The arena supplies its own thin net and edge marks. Keep every
                // existing collider transform intact when hiding the old artwork.
                if(IsVersus) { view.color=Color.clear;continue; }
                string name=view.name;
                if(name.Contains("wash")||name=="Upper boundary") view.color=Color.clear;
                else if(name.Contains("outer wall")) view.color=new Color(.23f,.38f,.29f,.28f);
                else if(name=="Left baseline") view.color=new Color(.64f,.94f,.80f,.25f);
                else if(name=="Right baseline") view.color=new Color(.94f,.76f,.49f,.25f);
                else if(name=="Low center net") view.color=new Color(.47f,.67f,.48f,.24f);
                else if(name=="Net top") view.color=new Color(.75f,.86f,.62f,.85f);
                else view.color=new Color(.60f,.75f,.55f,.36f);
            }
            if(panelTexture!=null)
            {
                SetTextureColor(panelTexture,IsVersus?new Color(.055f,.076f,.14f,.97f):new Color(.068f,.15f,.11f,.98f));
                SetTextureColor(buttonNormal,IsVersus?new Color(.08f,.12f,.19f):new Color(.105f,.21f,.155f));
                SetTextureColor(buttonHover,IsVersus?new Color(.13f,.21f,.29f):new Color(.17f,.30f,.23f));
                SetTextureColor(buttonPressed,IsVersus?new Color(.22f,.35f,.42f):new Color(.30f,.44f,.33f));
                smallStyle.normal.textColor=goalStyle.normal.textColor=IsVersus ? new Color(.65f,.72f,.83f) : new Color(.59f,.70f,.63f);
                leftScoreStyle.normal.textColor=PlayerColor(-1);
                rightScoreStyle.normal.textColor=PlayerColor(1);
                titleStyle.normal.textColor=bodyStyle.normal.textColor=centeredStyle.normal.textColor=
                    IsVersus ? new Color(.90f,.95f,1) : new Color(.95f,.95f,.86f);
                buttonStyle.normal.textColor=IsVersus ? new Color(.90f,.95f,1) : new Color(.89f,.94f,.87f);
                quietButtonStyle.normal.textColor=IsVersus ? new Color(.60f,.70f,.79f) : new Color(.66f,.76f,.69f);
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
