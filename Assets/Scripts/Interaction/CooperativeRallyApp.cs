using UnityEngine;
using Naispo.Core;
using Naispo.Kinect;
using Naispo.MultiKinect;
using Naispo.Silhouette;

namespace Naispo.Interaction
{
    [DefaultExecutionOrder(-500)]
    public sealed class CooperativeRallyApp : MonoBehaviour
    {
        public Camera gameCamera;
        public KinectSettings template;
        public MultiKinectSettings sensorTemplate;
        public Material silhouetteMaterial;
        public Sprite ballSprite;
        public Font japaneseFont;
        public RallyVisualConfig visualConfig;
        public CooperativeCoinGame Game {get;private set;}
        public KinectSilhouetteManager[] Players {get;private set;} = new KinectSilhouetteManager[2];
        public MultiKinectManager Sensors {get;private set;}
        public bool SensorMode {get;private set;}
        public bool SensorSettingsOpen {get;private set;}
        public bool BothPlayersReady => Ready(0) && Ready(1);
        readonly CooperativeDemoSource[] demos=new CooperativeDemoSource[2];
        readonly KinectPipeReceiver[] receivers=new KinectPipeReceiver[2];
        readonly KinectSettings[] playerSettings=new KinectSettings[2];
        readonly HandWallController[] handWalls=new HandWallController[2];
        MultiKinectSettings sensorSettings;
        string leftSerial,rightSerial;
        float minimumDistance,maximumDistance;
        Vector2 diagnosticScroll;
        string copyStatus="";
        GUIStyle label,small,button,selectedButton,field,title,statusLabel;
        GUISkin cameraSkin;
        Texture2D idleTexture,hoverTexture,pressedTexture,selectedTexture,fieldTexture,trackTexture;
        bool paletteInitialized,versusPalette;
        bool stylePaletteInitialized,versusStylePalette;
        Color cameraOverlayColor,cameraPanelColor;
        static readonly Color GardenBackground=new Color(.012f,.043f,.055f);
        static readonly Color VersusBackground=new Color(.043f,.027f,.074f);

        void Awake()
        {
            Application.runInBackground=true;
            Application.targetFrameRate=60;
            if(gameCamera==null)gameCamera=Camera.main;
            gameCamera.orthographic=true;gameCamera.orthographicSize=5;
            FitCamera();
            if(visualConfig==null)visualConfig=RallyVisualConfig.LoadDefault();
            gameCamera.backgroundColor=GardenBackground;
            sensorSettings=Instantiate(sensorTemplate);
            sensorSettings.showStatus=false;
            var defaults=sensorSettings.serialNumbers;
            leftSerial=PlayerPrefs.GetString("Coop.LeftSerial",defaults!=null&&defaults.Length>0 ? defaults[0]??"" : "");
            rightSerial=PlayerPrefs.GetString("Coop.RightSerial",defaults!=null&&defaults.Length>1 ? defaults[1]??"" : "");
            minimumDistance=PlayerPrefs.GetFloat("Coop.MinDistance",.5f);
            maximumDistance=PlayerPrefs.GetFloat("Coop.MaxDistance",4.5f);
            ApplySensorSettings();
            var ballObject=new GameObject("Shared rally ball");
            ballObject.SetActive(false);
            var ballView=ballObject.AddComponent<SpriteRenderer>();
            ballView.sprite=ballSprite;ballView.sortingOrder=20;
            ballObject.AddComponent<Rigidbody2D>();
            var circle=ballObject.AddComponent<CircleCollider2D>();circle.radius=.22f;
            var ball=ballObject.AddComponent<AirHockeyBall>();
            ballObject.SetActive(true);
            for(int slot=0;slot<2;slot++)BuildPlayer(slot);
            var sensorsObject=new GameObject("Two Kinect workers");sensorsObject.SetActive(false);
            Sensors=sensorsObject.AddComponent<MultiKinectManager>();
            Sensors.settings=sensorSettings;Sensors.receivers=receivers;
            Sensors.enabled=false;sensorsObject.SetActive(true);
            Game=gameObject.AddComponent<CooperativeCoinGame>();
            Game.BallLightRadius=template.ballLightRadius;
            // The visual configuration must exist before the first court build so the
            // initial art already uses the user's optional replacements.
            Game.VisualConfig=visualConfig;
            Game.Configure(ball,gameCamera,japaneseFont,visualConfig);
        }

        void BuildPlayer(int slot)
        {
            var color=PlayerColor(slot,false);
            var settings=Instantiate(template);playerSettings[slot]=settings;
            settings.maximumBodyCount=1;settings.showDebugOverlay=false;settings.debugLogging=false;
            settings.personColors=new[]{new Color(color.r*.25f,color.g*.25f,color.b*.25f,1),color,color};
            // The shared court owns one ball light across both player viewports.
            settings.barrierColors=new[]{color,color,color};settings.ballLightShader=null;
            // Demo coordinates are screen-space; physical inputs retain the mirror convention.
            settings.mirrorX=false;
            var root=new GameObject("Player "+(slot+1));root.SetActive(false);
            var mapper=root.AddComponent<KinectViewportMapper>();
            mapper.targetCamera=gameCamera;mapper.settings=settings;
            mapper.viewport=new Rect(slot*.5f,.04f,.5f,.72f);
            mapper.Refresh(320,288);
            var demo=root.AddComponent<CooperativeDemoSource>();
            demo.playerSlot=slot;demo.mapper=mapper;demos[slot]=demo;
            var receiver=root.AddComponent<KinectPipeReceiver>();
            receiver.enabled=false;receiver.playerSlot=slot;receivers[slot]=receiver;
            var viewObject=new GameObject("Silhouette");viewObject.transform.SetParent(root.transform,false);
            var renderer=viewObject.AddComponent<SilhouetteRenderer>();
            renderer.settings=settings;renderer.mapper=mapper;renderer.silhouetteMaterial=silhouetteMaterial;
            renderer.LightEnabled=false;
            SetPlayerOutline(renderer,color);
            var manager=root.AddComponent<KinectSilhouetteManager>();
            manager.autoAddCoinGame=false;manager.showControls=false;
            manager.settings=settings;manager.mapper=mapper;manager.silhouetteRenderer=renderer;
            // HandWallController uses this only to obtain its optional contact material.
            var colliders=root.AddComponent<SilhouetteColliderController>();
            colliders.enabled=false;colliders.settings=settings;colliders.mapper=mapper;colliders.manager=manager;
            manager.colliderController=colliders;manager.source=demo;manager.useHandWalls=true;
            Players[slot]=manager;root.SetActive(true);
            handWalls[slot]=root.GetComponent<HandWallController>();
            handWalls[slot].PlayerSide=slot==0 ? -1 : 1;
        }

        void Start()
        {
            var args=System.Environment.GetCommandLineArgs();
            if(System.Array.IndexOf(args,"--kinect")>=0)UseSensors();
        }

        void Update()
        {
            if(Game==null)return;
            FitCamera();
            ApplyPlayerPalette();
            bool ready=!SensorMode||BothPlayersReady;
            Game.ExternalOverlayOpen=SensorSettingsOpen;
            Game.SetInputReady(ready&&!SensorSettingsOpen);
            if(SensorMode&&Sensors.StartupState==KinectStartupState.Error)
            {
                Game.InputWaitTitle="接続エラー";
                Game.InputWaitDetail="カメラ設定を確認";
            }
            else if(SensorMode&&Sensors.StartupState!=KinectStartupState.Running)
            {
                Game.InputWaitTitle="接続中";
                Game.InputWaitDetail="";
            }
            else
            {
                Game.InputWaitTitle="準備中";
                Game.InputWaitDetail="両手を見せてください";
            }
            for(int i=0;i<2;i++)
            {
                demos[i].acceptControls=Application.isFocused&&!SensorSettingsOpen&&!Game.SettingsOpen&&!Game.IsPaused;
            }
            gameCamera.backgroundColor=Game.IsVersus ? VersusBackground : GardenBackground;
        }

        Color PlayerColor(int slot,bool versus)
        {
            var config=visualConfig;
            return slot==0
                ? (config!=null?config.playerJade:new Color(.64f,.94f,.80f))
                : (config!=null?config.playerGold:new Color(.94f,.76f,.49f));
        }

        static void SetPlayerOutline(SilhouetteRenderer renderer,Color color)
        {
            renderer.SetOutline(new Color(color.r*.75f,color.g*.75f,color.b*.75f,.8f),1.65f);
        }

        void ApplyPlayerPalette()
        {
            bool versus=Game.IsVersus;
            if(paletteInitialized&&versusPalette==versus)return;
            paletteInitialized=true;versusPalette=versus;
            for(int slot=0;slot<playerSettings.Length;slot++)
            {
                var color=PlayerColor(slot,versus);
                var settings=playerSettings[slot];
                const float silhouetteBrightness=.25f;
                // Reuse the three-slot palettes created with each player.
                for(int index=0;index<settings.barrierColors.Length;index++)settings.barrierColors[index]=color;
                for(int index=0;index<settings.personColors.Length;index++)
                    settings.personColors[index]=index==0 ? new Color(color.r*silhouetteBrightness,color.g*silhouetteBrightness,color.b*silhouetteBrightness,1) : color;
                SetPlayerOutline(Players[slot].silhouetteRenderer,color);
                handWalls[slot].SetPresentation(color,!versus);
            }
        }

        // Hand walls run at -600, then this -500 gate runs before ball physics.
        // A wall expiry and the ball pause must occur in the same physics tick.
        void FixedUpdate()
        {
            if(Game!=null)Game.SetInputReady((!SensorMode||BothPlayersReady)&&!SensorSettingsOpen);
        }

        bool Ready(int slot)
        {
            var manager=Players[slot];
            if(SensorMode)return manager!=null&&manager.isActiveAndEnabled&&handWalls[slot]!=null&&handWalls[slot].HasUsableWall;
            if(manager==null||!manager.IsFresh)return false;
            var frame=manager.CurrentFrame;
            return frame!=null&&frame.BodyCount>0&&
                HandWallController.TryEndpoints(frame.Hands[0],manager.mapper,out _,out _);
        }

        public string PlayerStatus(int slot)
        {
            if((uint)slot>=2)return "入力設定がありません";
            if(!SensorMode)return Ready(slot)?"キーボード操作中":"入力待ち";
            if(Sensors.StartupState==KinectStartupState.Error)return Sensors.LifecycleStatus;
            var receiver=receivers[slot];
            if(receiver.HasFailed)return "受信エラー: "+receiver.Status;
            if(Ready(slot))return handWalls[slot].HoldingLastPose ? "手の位置を短く保持中" : "認識中";
            if(!receiver.isActiveAndEnabled)return "カメラの接続を確認中";
            if(!receiver.Connected)return "カメラの起動待ち";
            if(receiver.ReceivedFrames==0)return "人体認識を初期化中";
            if(!Players[slot].IsFresh)return "データが途切れています";
            if(handWalls[slot].AcquiringHands)return "手の認識の安定を確認中";
            string handStatus=KinectHandFrameAdapter.DescribeReadiness(receiver.LatestMetadata);
            return string.IsNullOrEmpty(handStatus)?"両手を少し離してください":handStatus;
        }

        string FooterStatus(int slot)
        {
            if(Sensors.StartupState==KinectStartupState.Error)return "接続エラー";
            var receiver=receivers[slot];
            if(receiver.HasFailed)return "受信エラー";
            if(Ready(slot))return "READY";
            if(!receiver.isActiveAndEnabled||!receiver.Connected)return "接続中";
            if(receiver.ReceivedFrames==0)return "起動中";
            if(!Players[slot].IsFresh)return "接続待ち";
            if(handWalls[slot].AcquiringHands)return "認識中";
            return "両手待ち";
        }

        string DiagnosticSummary()
        {
            var text=new System.Text.StringBuilder("Kinect 接続診断\n");
            text.AppendLine(Sensors.LifecycleStatus).AppendLine().AppendLine(Sensors.DiscoveryReport);
            for(int slot=0;slot<2;slot++)
            {
                var receiver=receivers[slot];
                string requested=slot==0?leftSerial:rightSerial;
                text.AppendLine().AppendLine(slot==0?"左のカメラ":"右のカメラ");
                text.AppendLine("指定: "+(string.IsNullOrWhiteSpace(requested)?"自動":requested)+" / 接続: "+Sensors.AssignedSerial(slot));
                text.AppendLine(PlayerStatus(slot));
                text.AppendLine($"受信 {Sensors.ReceiveFps[slot]:F1} fps / 人体認識 {receiver.LatestMetadata.TrackingFps:F1} fps / {receiver.ReceivedFrames} フレーム");
                text.AppendLine("通信: "+receiver.Status);
                text.AppendLine(Sensors.WorkerDetail(slot));
            }
            text.AppendLine().AppendLine("記録フォルダー: "+Sensors.DiagnosticDirectory);
            return text.ToString();
        }

        public void UseDemo()
        {
            Sensors.StopWorkers();Sensors.enabled=false;SensorMode=false;
            for(int i=0;i<2;i++) {
                handWalls[i].ResetTracking();handWalls[i].StabilizeTracking=false;
                Players[i].enabled=false;receivers[i].enabled=false;demos[i].enabled=true;
                playerSettings[i].mirrorX=false;Players[i].source=demos[i];Players[i].enabled=true;
            }
            if(Game!=null)Game.RestartGame();
        }

        public void UseSensors()
        {
            Sensors.StopWorkers();Sensors.enabled=false;ApplySensorSettings();
            for(int i=0;i<2;i++) {
                handWalls[i].ResetTracking();handWalls[i].StabilizeTracking=true;
                Players[i].enabled=false;demos[i].enabled=false;
                playerSettings[i].mirrorX=true;Players[i].source=receivers[i];Players[i].enabled=true;
            }
            SensorMode=true;
            sensorSettings.launchMode=WorkerLaunchMode.AutomaticSensors;
            Sensors.enabled=true;
            if(Game!=null){Game.RestartGame();Game.SetInputReady(false);}
        }

        void ApplySensorSettings()
        {
            sensorSettings.serialNumbers=new[]{leftSerial.Trim(),rightSerial.Trim()};
            sensorSettings.minimumDistance=minimumDistance;sensorSettings.maximumDistance=maximumDistance;
        }

        void SaveSensorSettings()
        {
            PlayerPrefs.SetString("Coop.LeftSerial",leftSerial.Trim());PlayerPrefs.SetString("Coop.RightSerial",rightSerial.Trim());
            PlayerPrefs.SetFloat("Coop.MinDistance",minimumDistance);PlayerPrefs.SetFloat("Coop.MaxDistance",maximumDistance);PlayerPrefs.Save();
        }

        void OnGUI()
        {
            if(Game==null)return;
            Game.ExternalOverlayOpen=SensorSettingsOpen;
            EnsureStyles();
            var saved=GUI.matrix;
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
            var offset=new Vector2((Screen.width-1280*scale)*.5f,(Screen.height-720*scale)*.5f);
            GUI.matrix=Matrix4x4.TRS(offset,Quaternion.identity,new Vector3(scale,scale,1));
            if(SensorMode&&!Game.SettingsOpen&&!SensorSettingsOpen)
            {
                DrawPlayerStatus(0,new Rect(458,687,174,24));
                DrawPlayerStatus(1,new Rect(662,687,174,24));
            }
            if(SensorSettingsOpen)DrawSettings();
            Game.ExternalOverlayOpen=SensorSettingsOpen;
            GUI.matrix=saved;
        }

        // Called from the game settings in its existing 1280 x 720 GUI space.
        public void DrawInputControls(Rect area)
        {
            EnsureStyles();
            bool wasEnabled=GUI.enabled;
            GUI.enabled=wasEnabled&&!SensorSettingsOpen;
            float width=(area.width-16)/3;
            var demoRect=new Rect(area.x,area.y,width,36);
            var sensorRect=new Rect(area.x+width+8,area.y,width,36);
            var cameraRect=new Rect(area.x+2*(width+8),area.y,width,36);
            if(GUI.Button(demoRect,"DEMO",SensorMode ? button : selectedButton)&&SensorMode)UseDemo();
            if(GUI.Button(sensorRect,"KINECT",SensorMode ? selectedButton : button)&&!SensorMode)UseSensors();
            if(GUI.Button(cameraRect,"カメラ",button))OpenCameraSettings();
            if(area.height>=90)
            {
                if(SensorMode)
                {
                    DrawPlayerStatus(0,new Rect(area.x,area.y+48,area.width*.5f-8,26));
                    DrawPlayerStatus(1,new Rect(area.center.x+8,area.y+48,area.width*.5f-8,26));
                }
                else
                {
                    GUI.Label(new Rect(area.x,area.y+48,area.width,22),"L   WASD 移動   ·   Q E 傾き   ·   Z X 手幅",small);
                    GUI.Label(new Rect(area.x,area.y+72,area.width,22),"R   ↑↓←→ 移動   ·   J L 傾き   ·   U O 手幅",small);
                }
            }
            GUI.enabled=wasEnabled;
        }

        public void OpenCameraSettings()
        {
            SensorSettingsOpen=true;
            if(Game!=null)
            {
                Game.ExternalOverlayOpen=true;
                Game.SetInputReady(false);
            }
        }

        void DrawPlayerStatus(int slot,Rect area)
        {
            var color=PlayerColor(slot,Game.IsVersus);
            if(!Ready(slot))color.a=.4f;
            Fill(new Rect(area.x,area.center.y-2,4,4),color);
            GUI.Label(new Rect(area.x+14,area.y,area.width-14,area.height),(slot==0?"L   ":"R   ")+FooterStatus(slot),statusLabel);
        }

        void DrawSettings()
        {
            var savedSkin=GUI.skin;
            bool savedEnabled=GUI.enabled;
            GUI.enabled=true;
            GUI.skin=cameraSkin;
            Fill(new Rect(0,0,1280,720),cameraOverlayColor);
            Fill(new Rect(130,74,1020,604),cameraPanelColor);
            Color leftAccent=PlayerColor(0,Game.IsVersus),rightAccent=PlayerColor(1,Game.IsVersus);
            leftAccent.a=.55f;rightAccent.a=.4f;
            Fill(new Rect(130,74,60,1),leftAccent);
            Fill(new Rect(1090,677,60,1),rightAccent);
            GUI.Label(new Rect(162,94,620,40),"カメラの設定",title);
            GUI.Label(new Rect(162,148,440,44),"空欄なら接続中の2台を自動設定します。\n番号を入力すると、そのカメラを指定できます。",small);
            GUI.Label(new Rect(162,203,114,30),"左のカメラ",label);
            leftSerial=GUI.TextField(new Rect(286,198,316,36),leftSerial,32,field);
            GUI.Label(new Rect(162,253,114,30),"右のカメラ",label);
            rightSerial=GUI.TextField(new Rect(286,248,316,36),rightSerial,32,field);
            if(GUI.Button(new Rect(162,300,210,36),"自動設定",button)) { leftSerial=rightSerial=""; }
            if(GUI.Button(new Rect(390,300,212,36),"左右を入れ替える",button)) {
                string oldLeft=string.IsNullOrWhiteSpace(leftSerial)?Sensors.AssignedSerial(0):leftSerial;
                string oldRight=string.IsNullOrWhiteSpace(rightSerial)?Sensors.AssignedSerial(1):rightSerial;
                leftSerial=oldRight;rightSerial=oldLeft;
            }
            GUI.Label(new Rect(162,362,440,28),$"検出する距離   {minimumDistance:F1} ～ {maximumDistance:F1} m",label);
            GUI.Label(new Rect(162,407,80,28),"近い側",small);
            minimumDistance=GUI.HorizontalSlider(new Rect(246,418,356,20),minimumDistance,.5f,Mathf.Min(3,maximumDistance-.3f));
            GUI.Label(new Rect(162,453,80,28),"遠い側",small);
            maximumDistance=GUI.HorizontalSlider(new Rect(246,464,356,20),maximumDistance,Mathf.Max(1,minimumDistance+.3f),5);
            GUI.Label(new Rect(162,510,440,68),"接続後に左右が逆なら入れ替えて再接続。\nこのPCに設定を保存します。\n番号が違う場合は「自動設定」を選んでください。",small);
            string diagnostics=DiagnosticSummary();
            float contentHeight=small.CalcHeight(new GUIContent(diagnostics),436)+16;
            diagnosticScroll=GUI.BeginScrollView(new Rect(654,148,464,426),diagnosticScroll,new Rect(0,0,438,contentHeight));
            GUI.Label(new Rect(0,0,436,contentHeight),diagnostics,small);
            GUI.EndScrollView();
            if(GUI.Button(new Rect(162,602,272,42),"保存してKinectを開始",button)) {
                SaveSensorSettings();SensorSettingsOpen=false;UseSensors();
            }
            if(GUI.Button(new Rect(448,602,154,42),"閉じる",button))SensorSettingsOpen=false;
            if(GUI.Button(new Rect(654,602,220,42),"診断をコピー",button)) {
                GUIUtility.systemCopyBuffer=diagnostics;copyStatus="コピーしました";
            }
            GUI.Label(new Rect(892,610,226,28),copyStatus,small);
            GUI.skin=savedSkin;
            GUI.enabled=savedEnabled;
        }

        void EnsureStyles()
        {
            if(label!=null) { RefreshStylePalette();return; }
            var ink=new Color(.94f,.95f,.86f);
            var dim=new Color(.59f,.69f,.62f);
            idleTexture=FlatTexture(new Color(.07f,.16f,.11f));
            hoverTexture=FlatTexture(new Color(.13f,.25f,.18f));
            pressedTexture=FlatTexture(new Color(.20f,.34f,.24f));
            selectedTexture=FlatTexture(new Color(.79f,.88f,.74f));
            fieldTexture=FlatTexture(new Color(.025f,.075f,.05f));
            trackTexture=FlatTexture(new Color(.16f,.25f,.19f));
            label=new GUIStyle(GUI.skin.label){font=japaneseFont,fontSize=18,normal={textColor=ink}};
            small=new GUIStyle(label){fontSize=15,wordWrap=true,normal={textColor=dim}};
            statusLabel=new GUIStyle(small){fontSize=13,wordWrap=false,alignment=TextAnchor.MiddleLeft};
            title=new GUIStyle(label){fontSize=28,fontStyle=FontStyle.Normal};
            button=new GUIStyle(GUI.skin.button){font=japaneseFont,fontSize=17,border=new RectOffset(),padding=new RectOffset(10,10,4,4)};
            SetFlatStates(button,idleTexture,hoverTexture,pressedTexture,ink);
            selectedButton=new GUIStyle(button);
            SetFlatStates(selectedButton,selectedTexture,selectedTexture,pressedTexture,new Color(.07f,.16f,.11f));
            selectedButton.active.textColor=selectedButton.onActive.textColor=ink;
            field=new GUIStyle(GUI.skin.textField){font=japaneseFont,fontSize=20,border=new RectOffset(),padding=new RectOffset(8,8,6,6)};
            SetFlatStates(field,fieldTexture,idleTexture,idleTexture,ink);
            cameraSkin=Instantiate(GUI.skin);
            cameraSkin.hideFlags=HideFlags.HideAndDontSave;
            cameraSkin.horizontalSlider=new GUIStyle(GUI.skin.horizontalSlider){fixedHeight=4,border=new RectOffset(),margin=new RectOffset(0,0,8,8)};
            SetFlatStates(cameraSkin.horizontalSlider,trackTexture,trackTexture,trackTexture,ink);
            cameraSkin.horizontalSliderThumb=new GUIStyle(GUI.skin.horizontalSliderThumb){fixedWidth=12,fixedHeight=16,border=new RectOffset(),margin=new RectOffset(0,0,-6,0)};
            SetFlatStates(cameraSkin.horizontalSliderThumb,selectedTexture,selectedTexture,hoverTexture,ink);
            FlattenScrollStyle(cameraSkin.verticalScrollbar,fieldTexture,fieldTexture,fieldTexture);
            FlattenScrollStyle(cameraSkin.verticalScrollbarThumb,trackTexture,hoverTexture,pressedTexture);
            FlattenScrollStyle(cameraSkin.verticalScrollbarUpButton,fieldTexture,hoverTexture,pressedTexture);
            FlattenScrollStyle(cameraSkin.verticalScrollbarDownButton,fieldTexture,hoverTexture,pressedTexture);
            FlattenScrollStyle(cameraSkin.horizontalScrollbar,fieldTexture,fieldTexture,fieldTexture);
            FlattenScrollStyle(cameraSkin.horizontalScrollbarThumb,trackTexture,hoverTexture,pressedTexture);
            FlattenScrollStyle(cameraSkin.horizontalScrollbarLeftButton,fieldTexture,hoverTexture,pressedTexture);
            FlattenScrollStyle(cameraSkin.horizontalScrollbarRightButton,fieldTexture,hoverTexture,pressedTexture);
            RefreshStylePalette();
        }

        void RefreshStylePalette()
        {
            bool versus=Game!=null&&Game.IsVersus;
            if(stylePaletteInitialized&&versusStylePalette==versus)return;
            stylePaletteInitialized=true;versusStylePalette=versus;
            Color ink=versus ? new Color(.929f,.901f,.984f) : new Color(.94f,.95f,.86f);
            Color dim=versus ? new Color(.627f,.576f,.702f) : new Color(.59f,.69f,.62f);
            Color selectedInk=versus ? new Color(.10f,.075f,.16f) : new Color(.07f,.16f,.11f);
            cameraOverlayColor=versus ? new Color(.024f,.017f,.048f,.84f) : new Color(.014f,.037f,.030f,.84f);
            cameraPanelColor=versus ? new Color(.086f,.062f,.13f) : new Color(.035f,.095f,.085f);
            // Recolor the existing one-pixel surfaces only when the mode changes.
            SetTextureColor(idleTexture,versus ? new Color(.086f,.062f,.13f) : new Color(.045f,.12f,.11f));
            SetTextureColor(hoverTexture,versus ? new Color(.14f,.10f,.21f) : new Color(.08f,.20f,.18f));
            SetTextureColor(pressedTexture,versus ? new Color(.21f,.15f,.30f) : new Color(.13f,.30f,.26f));
            SetTextureColor(selectedTexture,versus ? new Color(.76f,.68f,.90f) : new Color(.79f,.88f,.74f));
            SetTextureColor(fieldTexture,versus ? new Color(.052f,.038f,.088f) : new Color(.018f,.060f,.060f));
            SetTextureColor(trackTexture,versus ? new Color(.17f,.13f,.25f) : new Color(.12f,.25f,.22f));
            SetTextColor(label,ink);SetTextColor(title,ink);
            SetTextColor(small,dim);SetTextColor(statusLabel,dim);
            SetTextColor(button,ink);SetTextColor(field,ink);
            SetTextColor(selectedButton,selectedInk);
            selectedButton.active.textColor=selectedButton.onActive.textColor=ink;
            cameraSkin.settings.cursorColor=ink;
        }

        static void SetTextureColor(Texture2D texture,Color color)
        {
            texture.SetPixel(0,0,color);texture.Apply();
        }

        static void SetTextColor(GUIStyle style,Color color)
        {
            style.normal.textColor=style.hover.textColor=style.active.textColor=style.focused.textColor=color;
            style.onNormal.textColor=style.onHover.textColor=style.onActive.textColor=style.onFocused.textColor=color;
        }

        static Texture2D FlatTexture(Color color)
        {
            var texture=new Texture2D(1,1,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp};
            texture.SetPixel(0,0,color);texture.Apply();return texture;
        }

        static void SetFlatStates(GUIStyle style,Texture2D normal,Texture2D hover,Texture2D active,Color color)
        {
            style.normal.background=normal;style.hover.background=hover;style.active.background=active;style.focused.background=hover;
            style.onNormal.background=normal;style.onHover.background=hover;style.onActive.background=active;style.onFocused.background=hover;
            style.normal.textColor=style.hover.textColor=style.active.textColor=style.focused.textColor=color;
            style.onNormal.textColor=style.onHover.textColor=style.onActive.textColor=style.onFocused.textColor=color;
        }

        static void FlattenScrollStyle(GUIStyle style,Texture2D normal,Texture2D hover,Texture2D active)
        {
            style.border=new RectOffset();
            SetFlatStates(style,normal,hover,active,Color.white);
        }

        static void Fill(Rect rect,Color color){var old=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old;}
        void FitCamera()
        {
            float screenAspect=(float)Screen.width/Mathf.Max(1,Screen.height), target=16f/9;
            gameCamera.rect=screenAspect>target ? new Rect((1-target/screenAspect)*.5f,0,target/screenAspect,1)
                : new Rect(0,(1-screenAspect/target)*.5f,1,screenAspect/target);
            gameCamera.aspect=target;
        }
        void OnDestroy()
        {
            if(Sensors!=null)Sensors.StopWorkers();
            foreach(var settings in playerSettings)if(settings!=null)Destroy(settings);
            if(sensorSettings!=null)Destroy(sensorSettings);
            if(cameraSkin!=null)Destroy(cameraSkin);
            if(idleTexture!=null)Destroy(idleTexture);
            if(hoverTexture!=null)Destroy(hoverTexture);
            if(pressedTexture!=null)Destroy(pressedTexture);
            if(selectedTexture!=null)Destroy(selectedTexture);
            if(fieldTexture!=null)Destroy(fieldTexture);
            if(trackTexture!=null)Destroy(trackTexture);
        }
    }
}
