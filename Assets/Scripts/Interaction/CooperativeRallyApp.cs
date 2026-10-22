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
        MultiKinectSettings sensorSettings;
        string leftSerial,rightSerial;
        float minimumDistance,maximumDistance;
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
            leftSerial=PlayerPrefs.GetString("Coop.LeftSerial",sensorSettings.serialNumbers[0]);
            rightSerial=PlayerPrefs.GetString("Coop.RightSerial",sensorSettings.serialNumbers[1]);
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
            for(int i=0;i<2;i++)demos[i].acceptControls=Application.isFocused&&!SensorSettingsOpen&&!Game.SettingsOpen&&!Game.IsPaused;
            gameCamera.backgroundColor=new Color(.018f,.028f,.065f);
        }

        bool Ready(int slot)
        {
            var manager=Players[slot];
            if(manager==null||!manager.IsFresh)return false;
            var frame=manager.CurrentFrame;
            return frame!=null&&frame.BodyCount>0&&
                HandWallController.TryEndpoints(frame.Hands[0],manager.mapper,out _,out _);
        }

        public void UseDemo()
        {
            Sensors.StopWorkers();Sensors.enabled=false;SensorMode=false;
            for(int i=0;i<2;i++) {
                Players[i].enabled=false;receivers[i].enabled=false;demos[i].enabled=true;
                playerSettings[i].mirrorX=false;Players[i].source=demos[i];Players[i].enabled=true;
            }
            if(Game!=null)Game.RestartGame();
        }

        public void UseSensors()
        {
            Sensors.StopWorkers();Sensors.enabled=false;ApplySensorSettings();
            for(int i=0;i<2;i++) {
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
            if(GUI.Button(new Rect(24,635,170,38),SensorMode?"キーボードに戻る":"キーボード操作",button))UseDemo();
            if(GUI.Button(new Rect(204,635,142,38),SensorMode?"Kinectを再接続":"Kinect 2台で遊ぶ",button))UseSensors();
            if(GUI.Button(new Rect(356,635,124,38),"カメラの設定",button))SensorSettingsOpen=true;
            GUI.Label(new Rect(506,630,355,30),SensorMode?"左の人  "+(Ready(0)?"● 認識中":"両手をカメラへ"):"左の人  WASD 移動 / Q・E 傾き",label);
            GUI.Label(new Rect(888,630,370,30),SensorMode?"右の人  "+(Ready(1)?"● 認識中":"両手をカメラへ"):"右の人  ↑↓←→ 移動 / J・L 傾き",label);
            GUI.Label(new Rect(506,666,355,28),SensorMode?"手を開いてバリアを出そう":"Z・X 手幅  ／  マウスでも移動",small);
            GUI.Label(new Rect(888,666,370,28),SensorMode?"2人そろうとボールが動きます":"U・O 手幅  ／  マウスでも移動",small);
            GUI.Label(new Rect(24,682,460,26),SensorMode?"KINECT 2台  ·  協力プレイ":"DEMO  ·  カメラを使わず2人で操作できます",small);
            GUI.enabled=true;
            if(SensorSettingsOpen)DrawSettings();
            Game.ExternalOverlayOpen=SensorSettingsOpen;
            GUI.matrix=saved;
        }

        void DrawSettings()
        {
            Fill(new Rect(0,0,1280,720),new Color(.006f,.010f,.025f,.90f));
            Fill(new Rect(290,116,700,488),new Color(.05f,.08f,.14f));
            GUI.Label(new Rect(326,136,620,40),"カメラの設定",title);
            GUI.Label(new Rect(326,187,620,30),"各カメラが1人ずつ担当します。左右が逆なら入れ替えてください。",small);
            GUI.Label(new Rect(326,230,114,30),"左のカメラ",label);
            leftSerial=GUI.TextField(new Rect(450,225,280,36),leftSerial,32,field);
            GUI.Label(new Rect(326,279,114,30),"右のカメラ",label);
            rightSerial=GUI.TextField(new Rect(450,274,280,36),rightSerial,32,field);
            if(GUI.Button(new Rect(754,225,182,85),"左右を入れ替える",button)) {
                var serial=leftSerial;leftSerial=rightSerial;rightSerial=serial;
            }
            GUI.Label(new Rect(326,331,620,28),$"検出する距離   {minimumDistance:F1} ～ {maximumDistance:F1} m",label);
            GUI.Label(new Rect(326,369,80,28),"近い側",small);
            minimumDistance=GUI.HorizontalSlider(new Rect(410,380,526,20),minimumDistance,.5f,Mathf.Min(3,maximumDistance-.3f));
            GUI.Label(new Rect(326,407,80,28),"遠い側",small);
            maximumDistance=GUI.HorizontalSlider(new Rect(410,418,526,20),maximumDistance,Mathf.Max(1,minimumDistance+.3f),5);
            GUI.Label(new Rect(326,448,610,52),SensorMode?Sensors.LifecycleStatus:"カメラ本体のシリアル番号を入力。設定はこのPCに保存されます。",small);
            if(GUI.Button(new Rect(326,526,292,44),"保存してKinectを開始",button)) {
                SaveSensorSettings();SensorSettingsOpen=false;UseSensors();
            }
            if(GUI.Button(new Rect(638,526,298,44),"閉じる",button))SensorSettingsOpen=false;
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
