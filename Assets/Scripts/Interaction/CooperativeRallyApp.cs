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
        GUIStyle label,small,button,field,title;

        void Awake()
        {
            Application.runInBackground=true;
            Application.targetFrameRate=60;
            if(gameCamera==null)gameCamera=Camera.main;
            gameCamera.orthographic=true;gameCamera.orthographicSize=5;
            FitCamera();
            gameCamera.backgroundColor=new Color(.018f,.028f,.065f);
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
            Game.Configure(ball,gameCamera,japaneseFont);
        }

        void BuildPlayer(int slot)
        {
            var color=slot==0?new Color(.25f,.88f,.94f):new Color(1,.50f,.46f);
            var settings=Instantiate(template);playerSettings[slot]=settings;
            settings.maximumBodyCount=1;settings.showDebugOverlay=false;settings.debugLogging=false;
            settings.personColors=new[]{new Color(color.r*.40f,color.g*.40f,color.b*.40f,1),color,color};
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
                handWalls[i].Softened=Game.IsVersus&&Game.VersusState.SoftenedSide==(i==0 ? -1 : 1);
            }
            gameCamera.backgroundColor=new Color(.018f,.028f,.065f);
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
            GUI.enabled=!Game.SettingsOpen&&!SensorSettingsOpen;
            Fill(new Rect(0,620,1280,100),new Color(.035f,.053f,.095f,.98f));
            if(GUI.Button(new Rect(24,638,124,38),"DEMO",button))UseDemo();
            if(GUI.Button(new Rect(160,638,124,38),"KINECT",button))UseSensors();
            if(GUI.Button(new Rect(296,638,100,38),"カメラ",button))SensorSettingsOpen=true;
            GUI.enabled=true;
            Fill(new Rect(SensorMode?160:24,680,124,3),new Color(.25f,.88f,.94f,.8f));
            if(SensorMode)
            {
                DrawPlayerStatus(0,506);
                DrawPlayerStatus(1,888);
            }
            else if(Game.IsPaused||Game.SettingsOpen)
            {
                GUI.Label(new Rect(506,632,355,30),"L  WASD 移動 · Q E 傾き",label);
                GUI.Label(new Rect(888,632,370,30),"R  ↑↓←→ 移動 · J L 傾き",label);
                GUI.Label(new Rect(506,666,355,28),"Z X 手幅 · マウス 移動",small);
                GUI.Label(new Rect(888,666,370,28),"U O 手幅 · マウス 移動",small);
            }
            if(SensorSettingsOpen)DrawSettings();
            Game.ExternalOverlayOpen=SensorSettingsOpen;
            GUI.matrix=saved;
        }

        void DrawPlayerStatus(int slot,float x)
        {
            var color=slot==0?new Color(.25f,.88f,.94f):new Color(1,.50f,.46f);
            if(!Ready(slot))color.a=.4f;
            Fill(new Rect(x,646,4,22),color);
            GUI.Label(new Rect(x+18,640,338,34),(slot==0?"L   ":"R   ")+FooterStatus(slot),label);
        }

        void DrawSettings()
        {
            Fill(new Rect(0,0,1280,720),new Color(.006f,.010f,.025f,.90f));
            Fill(new Rect(130,74,1020,604),new Color(.05f,.08f,.14f));
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
        }

        void EnsureStyles()
        {
            if(label!=null)return;
            label=new GUIStyle(GUI.skin.label){font=japaneseFont,fontSize=18,normal={textColor=new Color(.86f,.93f,1)}};
            small=new GUIStyle(label){fontSize=15,wordWrap=true,normal={textColor=new Color(.60f,.71f,.85f)}};
            title=new GUIStyle(label){fontSize=30,fontStyle=FontStyle.Bold};
            button=new GUIStyle(GUI.skin.button){font=japaneseFont,fontSize=17};
            field=new GUIStyle(GUI.skin.textField){font=japaneseFont,fontSize=20,padding=new RectOffset(8,8,6,6)};
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
        }
    }
}
