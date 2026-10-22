using UnityEngine;
using UnityEngine.SceneManagement;

namespace Naispo.Interaction
{
    // Shared progress is deliberately independent of sensors and rendering.
    public sealed class CooperativeCoinRound
    {
        readonly bool[] collected;
        public int Round { get; private set; } = 1;
        public int TeamScore { get; private set; }
        public int CollectedCount { get; private set; }
        public int Misses { get; private set; }
        public int NextServeSide { get; private set; } = -1;
        public int CoinCount => collected.Length;
        public bool Complete => CollectedCount == collected.Length;

        public CooperativeCoinRound(int count) { collected = new bool[Mathf.Max(1,count)]; }
        public bool IsCollected(int index) => collected[index];
        public bool Collect(int index)
        {
            if(index < 0 || index >= collected.Length || collected[index]) return false;
            collected[index] = true; CollectedCount++; TeamScore++;
            return true;
        }
        public void RegisterMiss() { Misses++; }
        public int TakeServeSide() { int side = NextServeSide; NextServeSide = -side; return side; }
        public bool AdvanceRound()
        {
            if(!Complete) return false;
            Round++; CollectedCount = 0;
            System.Array.Clear(collected,0,collected.Length);
            return true;
        }
        public void Reset()
        {
            Round=1; TeamScore=CollectedCount=Misses=0; NextServeSide=-1;
            System.Array.Clear(collected,0,collected.Length);
        }
    }

    /// <summary>Two players return one ball over a low net and collect a shared arc of coins.</summary>
    public sealed class CooperativeCoinGame : MonoBehaviour
    {
        public const int CoinsPerRound = 9;
        public Font GuiFont;
        [SerializeField, Range(1f,3.1f)] float netHeight = 2f;
        [SerializeField, Range(6.5f,10.5f)] float ballSpeed = 8.4f;
        [SerializeField] bool ballLightEnabled = true;
        [SerializeField, Range(.5f,4f)] float ballLightRadius = 1.8f;
        readonly CooperativeCoinRound progress = new CooperativeCoinRound(CoinsPerRound);
        readonly SpriteRenderer[] coins = new SpriteRenderer[CoinsPerRound];
        readonly SpriteRenderer[] glows = new SpriteRenderer[CoinsPerRound];
        readonly float[] pickupAges = new float[CoinsPerRound];
        readonly Vector2[] coinPositions = new Vector2[CoinsPerRound];
        AirHockeyBall ball;
        Camera cameraView;
        SpriteRenderer ballView;
        CircleCollider2D ballCollider;
        GameObject arenaRoot;
        BoxCollider2D net;
        Transform netCap;
        SpriteRenderer ballLight, ballCorona;
        Sprite coinSprite, whiteSprite, haloSprite, lightSprite;
        Texture2D coinTexture, whiteTexture, haloTexture, lightTexture;
        PhysicsMaterial2D wallMaterial;
        GUIStyle titleStyle, eyebrowStyle, bodyStyle, scoreStyle, buttonStyle, centeredStyle, smallStyle;
        Texture2D panelTexture;
        Vector2 center, previous;
        float halfWidth, halfHeight, serveRemaining=-1, clearRemaining=-1, animationTime;
        int servingSide=-1;
        bool configured, paused, inputReady=true, settingsOpen;

        public int TeamScore => progress.TeamScore;
        public int Round => progress.Round;
        public int CollectedCount => progress.CollectedCount;
        public int Misses => progress.Misses;
        public bool IsPaused => paused;
        public bool InputReady => inputReady;
        public bool SettingsOpen => settingsOpen;
        public bool ExternalOverlayOpen { get; set; }
        public bool WaitingToServe => serveRemaining >= 0;
        public bool RoundComplete => progress.Complete;
        public float ArenaFloor => center.y-halfHeight+0.6f;
        public float HalfWidth => halfWidth;
        public BoxCollider2D NetCollider => net;
        public bool BallLightEnabled
        {
            get => ballLightEnabled;
            set
            {
                if(ballLightEnabled==value) return;
                ballLightEnabled=value;
                // Switching on must not collect along a path travelled in darkness.
                if(ball != null) previous=ball.Body.position;
                if(configured) RefreshLighting();
            }
        }
        public float BallLightRadius
        {
            get => ballLightRadius;
            set { ballLightRadius=Mathf.Clamp(value,.5f,4f); if(configured) RefreshLighting(); }
        }
        public float NetHeight
        {
            get => netHeight;
            set { netHeight=Mathf.Clamp(value,1f,3.1f); if(configured) RefreshNet(); }
        }
        public float BallSpeed
        {
            get => ballSpeed;
            set { ballSpeed=Mathf.Clamp(value,6.5f,10.5f); if(ball != null) ball.maximumSpeed=ballSpeed*1.12f; }
        }

        public void Configure(AirHockeyBall gameBall, Camera view, Font japaneseFont = null)
        {
            if(configured) return;
            ball=gameBall; cameraView=view; GuiFont=japaneseFont != null ? japaneseFont : GuiFont;
            if(ball == null || cameraView == null || !cameraView.orthographic) return;
            center=cameraView.transform.position;
            halfHeight=cameraView.orthographicSize;
            halfWidth=halfHeight*cameraView.aspect-0.35f;
            ballView=ball.GetComponent<SpriteRenderer>();
            ballCollider=ball.GetComponent<CircleCollider2D>();
            ball.GravityGame=true; ball.CooperativeGame=this;
            ball.Body.gravityScale=0.55f;
            ball.Body.linearDamping=0.015f;
            ball.maximumSpeed=ballSpeed*1.12f;
            BuildArtwork(); BuildArena(); BuildCoins(); BuildBallLight();
            configured=true;
            RefreshNet(); PlaceCoins(); BeginServe(1.4f);
        }

        void BuildArtwork()
        {
            whiteTexture=new Texture2D(1,1,TextureFormat.RGBA32,false);
            whiteTexture.SetPixel(0,0,Color.white); whiteTexture.Apply();
            whiteSprite=Sprite.Create(whiteTexture,new Rect(0,0,1,1),Vector2.one*0.5f,1);
            coinTexture=new Texture2D(64,64,TextureFormat.RGBA32,false);
            haloTexture=new Texture2D(64,64,TextureFormat.RGBA32,false);
            lightTexture=new Texture2D(128,128,TextureFormat.RGBA32,false) { wrapMode=TextureWrapMode.Clamp };
            for(int y=0;y<64;y++) for(int x=0;x<64;x++)
            {
                float dx=x-31.5f, dy=y-31.5f;
                float distance=Mathf.Sqrt(dx*dx+dy*dy);
                Color c=Color.clear;
                if(distance<29)
                {
                    c=distance>25 ? new Color(1,.69f,.22f) : new Color(1,.87f,.44f);
                    if(distance<22 && distance>20) c=new Color(.88f,.57f,.12f);
                    if(Mathf.Abs(dx)<2.2f && Mathf.Abs(dy)<12) c=new Color(1,.98f,.79f);
                    c.a=Mathf.Clamp01(29-distance);
                }
                coinTexture.SetPixel(x,y,c);
                haloTexture.SetPixel(x,y,new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-distance/32),2)));
            }
            coinTexture.Apply(); haloTexture.Apply();
            for(int y=0;y<128;y++) for(int x=0;x<128;x++)
            {
                float distance=Vector2.Distance(new Vector2(x,y),new Vector2(63.5f,63.5f))/64;
                lightTexture.SetPixel(x,y,new Color(1,1,1,RevealAmount(distance,1)));
            }
            lightTexture.Apply();
            coinSprite=Sprite.Create(coinTexture,new Rect(0,0,64,64),Vector2.one*.5f,100);
            haloSprite=Sprite.Create(haloTexture,new Rect(0,0,64,64),Vector2.one*.5f,64);
            lightSprite=Sprite.Create(lightTexture,new Rect(0,0,128,128),Vector2.one*.5f,128);
        }

        SpriteRenderer Rectangle(string name, Vector2 position, Vector2 size, Color color, int order)
        {
            var go=CreateChild(name,arenaRoot.transform);
            go.transform.position=new Vector3(position.x,position.y,0);
            go.transform.localScale=new Vector3(size.x,size.y,1);
            var view=go.AddComponent<SpriteRenderer>();
            view.sprite=whiteSprite; view.color=color; view.sortingOrder=order;
            return view;
        }

        GameObject CreateChild(string name,Transform parent)
        {
            var go=new GameObject(name);
            // Preserve the component's physics scene when hosted in an additive court.
            SceneManager.MoveGameObjectToScene(go,gameObject.scene);
            go.transform.SetParent(parent,false);
            return go;
        }

        BoxCollider2D Solid(string name,Vector2 position,Vector2 size,Color color)
        {
            var view=Rectangle(name,position,size,color,1);
            var collider=view.gameObject.AddComponent<BoxCollider2D>();
            collider.size=Vector2.one; collider.sharedMaterial=wallMaterial;
            return collider;
        }

        void BuildArena()
        {
            arenaRoot=CreateChild("Cooperative court",transform);
            wallMaterial=new PhysicsMaterial2D("Cooperative court bounce") { friction=0, bounciness=.84f,
                bounceCombine=PhysicsMaterialCombine2D.Minimum };
            Color cyan=new Color(.25f,.86f,.91f), coral=new Color(1,.46f,.40f);
            Rectangle("Left court wash",center+new Vector2(-halfWidth*.5f,-.35f),
                new Vector2(halfWidth,halfHeight*1.62f),new Color(.08f,.25f,.34f,.19f),-10);
            Rectangle("Right court wash",center+new Vector2(halfWidth*.5f,-.35f),
                new Vector2(halfWidth,halfHeight*1.62f),new Color(.35f,.14f,.24f,.17f),-10);
            Solid("Left outer wall",center+new Vector2(-halfWidth-.12f,-.3f),
                new Vector2(.24f,halfHeight*2),new Color(.13f,.28f,.37f));
            Solid("Right outer wall",center+new Vector2(halfWidth+.12f,-.3f),
                new Vector2(.24f,halfHeight*2),new Color(.32f,.20f,.29f));
            Solid("Upper boundary",center+new Vector2(0,halfHeight-.85f),
                new Vector2(halfWidth*2,.12f),new Color(.13f,.18f,.28f));
            Rectangle("Left baseline",new Vector2(center.x-halfWidth*.5f,ArenaFloor),
                new Vector2(halfWidth-.25f,.035f),new Color(cyan.r,cyan.g,cyan.b,.55f),0);
            Rectangle("Right baseline",new Vector2(center.x+halfWidth*.5f,ArenaFloor),
                new Vector2(halfWidth-.25f,.035f),new Color(coral.r,coral.g,coral.b,.55f),0);
            net=Solid("Low center net",Vector2.zero,new Vector2(.18f,netHeight),new Color(.46f,.57f,.72f,.8f));
            netCap=Rectangle("Net top",Vector2.zero,new Vector2(.42f,.10f),new Color(.82f,.9f,1),3).transform;
            for(int i=1;i<8;i++)
            {
                var mark=Rectangle("Net weave "+i,Vector2.zero,new Vector2(.2f,.025f),new Color(.78f,.85f,.95f,.65f),2);
                mark.transform.SetParent(net.transform,false);
                mark.transform.localPosition=new Vector3(0,(float)i/8-.5f,-.01f);
                mark.transform.localScale=new Vector3(1.2f,.018f,1);
            }
        }

        void RefreshNet()
        {
            net.transform.position=new Vector3(center.x,ArenaFloor+netHeight*.5f,0);
            net.transform.localScale=new Vector3(.18f,netHeight,1);
            netCap.position=new Vector3(center.x,ArenaFloor+netHeight,0);
        }

        void BuildCoins()
        {
            for(int i=0;i<CoinsPerRound;i++)
            {
                var go=CreateChild("Shared coin "+(i+1),arenaRoot.transform);
                coins[i]=go.AddComponent<SpriteRenderer>(); coins[i].sprite=coinSprite; coins[i].sortingOrder=7;
                var glow=CreateChild("Warm halo",go.transform);
                glows[i]=glow.AddComponent<SpriteRenderer>(); glows[i].sprite=haloSprite;
                glows[i].sortingOrder=6; glows[i].color=new Color(1,.71f,.2f,.35f);
                glow.transform.localScale=Vector3.one*1.7f;
            }
        }

        void BuildBallLight()
        {
            ballLight=CreateChild("Ball light",arenaRoot.transform).AddComponent<SpriteRenderer>();
            ballLight.sprite=lightSprite; ballLight.sortingOrder=4;
            ballLight.color=new Color(1,.84f,.50f,.17f);
            ballCorona=CreateChild("Ball corona",arenaRoot.transform).AddComponent<SpriteRenderer>();
            ballCorona.sprite=haloSprite; ballCorona.sortingOrder=8;
            ballCorona.color=new Color(1,.89f,.58f,.85f);
            ballCorona.transform.localScale=Vector3.one*1.5f;
        }

        public static Vector2 ParabolicCoinPosition(float t, int round, Vector2 courtCenter, float courtHalfWidth, float courtHalfHeight)
        {
            float u=Mathf.Clamp01(t)*2-1;
            float apex=courtHalfHeight*(round%3==2 ? .43f : round%3==0 ? .52f : .48f);
            float edge=-courtHalfHeight*.06f;
            float width=courtHalfWidth*(round%3==0 ? .49f : .54f);
            return courtCenter+new Vector2(u*width,Mathf.Lerp(edge,apex,1-u*u));
        }

        void PlaceCoins()
        {
            for(int i=0;i<CoinsPerRound;i++)
            {
                coinPositions[i]=ParabolicCoinPosition((float)i/(CoinsPerRound-1),Round,center,halfWidth,halfHeight);
                coins[i].transform.position=coinPositions[i]; coins[i].enabled=false; glows[i].enabled=false;
                coins[i].transform.localScale=Vector3.one; pickupAges[i]=-1;
            }
        }

        public Vector2 ServePosition(int side) => new Vector2(center.x+Mathf.Sign(side)*halfWidth*.52f,center.y-.2f);

        void BeginServe(float delay)
        {
            servingSide=progress.TakeServeSide();
            serveRemaining=delay;
            Vector2 spawn=ServePosition(servingSide);
            ball.Body.position=spawn; ball.transform.position=new Vector3(spawn.x,spawn.y,ball.transform.position.z);
            ball.Body.linearVelocity=Vector2.zero; ball.Body.angularVelocity=0;
            previous=spawn;
            ballView.enabled=true;
            ApplySimulationState();
            RefreshLighting();
        }

        public void SetInputReady(bool ready)
        {
            inputReady=ready;
            ApplySimulationState();
        }
        public void SetPaused(bool value) { paused=value; ApplySimulationState(); }
        void ApplySimulationState()
        {
            if(ball != null) ball.Body.simulated=configured && inputReady && !paused && !settingsOpen && serveRemaining<0 && clearRemaining<0;
        }
        public void RestartGame()
        {
            if(!configured) return;
            progress.Reset(); clearRemaining=-1; paused=false;
            PlaceCoins(); BeginServe(1.2f);
        }

        void Update()
        {
            if(!configured) return;
            if(!inputReady || paused || settingsOpen) return;
            if(clearRemaining>=0)
            {
                clearRemaining-=Time.deltaTime;
                if(clearRemaining<=0)
                {
                    clearRemaining=-1; progress.AdvanceRound(); PlaceCoins(); BeginServe(1.2f);
                }
                return;
            }
            if(serveRemaining>=0)
            {
                serveRemaining-=Time.deltaTime;
                if(serveRemaining<=0)
                {
                    serveRemaining=-1;
                    ball.Body.linearVelocity=new Vector2(-servingSide*.25f,-.5f);
                    ApplySimulationState();
                }
                return;
            }
            Vector2 current=ball.Body.position;
            if(current.y<ArenaFloor-.8f)
            {
                progress.RegisterMiss(); BeginServe(1.2f);
                return;
            }
            float radius=ballCollider.bounds.extents.x+.27f;
            for(int i=0;i<CoinsPerRound;i++)
            {
                if(ballLightEnabled && !progress.IsCollected(i) && SweptHit(previous,current,coinPositions[i],radius) && progress.Collect(i))
                    pickupAges[i]=0;
            }
            previous=current;
            if(progress.Complete)
            {
                clearRemaining=2.0f; ApplySimulationState();
            }
        }

        void LateUpdate()
        {
            if(!configured) return;
            animationTime+=Time.unscaledDeltaTime;
            for(int i=0;i<CoinsPerRound;i++)
            {
                if(pickupAges[i]>=0)
                {
                    pickupAges[i]+=Time.unscaledDeltaTime;
                    float fade=Mathf.Clamp01(1-pickupAges[i]/.35f);
                    coins[i].transform.localScale=Vector3.one*(1+(1-fade)*.8f);
                }
                else
                {
                    float pulse=.96f+.04f*Mathf.Sin(animationTime*2.5f+i*.65f);
                    coins[i].transform.localScale=Vector3.one*pulse;
                }
            }
            RefreshLighting();
        }

        // Match the original light-coin game's full inner light and soft outer edge.
        public static float RevealAmount(float distance,float radius)
        {
            if(radius<=0) return 0;
            return LightCoinGame.RevealAmount(distance,radius);
        }

        public float CoinVisibility(int index)
        {
            if(!configured || !ballLightEnabled || index<0 || index>=CoinsPerRound) return 0;
            return RevealAmount(Vector2.Distance(coinPositions[index],ball.transform.position),ballLightRadius);
        }

        void RefreshLighting()
        {
            ballLight.transform.position=ball.transform.position;
            ballLight.transform.localScale=Vector3.one*(ballLightRadius*2);
            ballCorona.transform.position=ball.transform.position;
            ballLight.enabled=ballCorona.enabled=ballLightEnabled && ballView.enabled;
            for(int i=0;i<CoinsPerRound;i++)
            {
                float fade=pickupAges[i]>=0 ? Mathf.Clamp01(1-pickupAges[i]/.35f) : 1;
                float alpha=CoinVisibility(i)*fade;
                coins[i].enabled=glows[i].enabled=alpha>.001f;
                coins[i].color=new Color(1,1,1,alpha);
                glows[i].color=new Color(1,.76f,.25f,alpha*(pickupAges[i]>=0 ? .55f : .32f));
            }
            if(serveRemaining>=0) ballView.color=new Color(1,1,1,.65f+.35f*Mathf.Sin(animationTime*5)*Mathf.Sin(animationTime*5));
            else ballView.color=Color.white;
        }

        public static bool SweptHit(Vector2 from,Vector2 to,Vector2 coin,float radius)
        {
            Vector2 delta=to-from;
            float t=delta.sqrMagnitude>0 ? Mathf.Clamp01(Vector2.Dot(coin-from,delta)/delta.sqrMagnitude) : 0;
            return (coin-(from+delta*t)).sqrMagnitude<=radius*radius;
        }

        public Vector2 BarrierBounce(Vector2 velocity,Vector2 normal,float softness,Vector2 position)
        {
            return CooperativeReflection(velocity,normal,softness,position.x-center.x,ballSpeed);
        }
        public static Vector2 CooperativeReflection(Vector2 velocity,Vector2 normal,float softness,float ballX,float speed)
        {
            normal.Normalize();
            // A flat barrier gives a gentle pass. Players can steer by tilting either hand.
            // Underside contacts retain their downward direction instead of teleporting upward.
            if(normal.y<.05f)
                return Vector2.ClampMagnitude(velocity+normal*Mathf.Max(0,speed*.85f-Vector2.Dot(velocity,normal)),speed*1.12f);
            float inward=ballX<0 ? 1f : -1f;
            float aim=Mathf.Clamp(inward*.43f+normal.x*.85f+velocity.x*.012f,-.85f,.85f);
            Vector2 direction=new Vector2(aim,Mathf.Max(.62f,normal.y)).normalized;
            return direction*speed*Mathf.Lerp(1.04f,.88f,Mathf.Clamp01(softness));
        }

        void EnsureStyles()
        {
            if(titleStyle != null) return;
            titleStyle=new GUIStyle(GUI.skin.label) { font=GuiFont,fontSize=25,fontStyle=FontStyle.Bold,normal={textColor=new Color(.95f,.97f,1)} };
            eyebrowStyle=new GUIStyle(titleStyle) { fontSize=12,fontStyle=FontStyle.Normal,normal={textColor=new Color(.48f,.71f,.88f)} };
            bodyStyle=new GUIStyle(titleStyle) { fontSize=17,fontStyle=FontStyle.Normal };
            smallStyle=new GUIStyle(bodyStyle) { fontSize=13,normal={textColor=new Color(.65f,.72f,.83f)} };
            scoreStyle=new GUIStyle(titleStyle) { fontSize=30,alignment=TextAnchor.MiddleRight };
            centeredStyle=new GUIStyle(titleStyle) { alignment=TextAnchor.MiddleCenter,fontSize=24 };
            buttonStyle=new GUIStyle(GUI.skin.button) { font=GuiFont,fontSize=15,fontStyle=FontStyle.Bold };
            panelTexture=new Texture2D(1,1); panelTexture.SetPixel(0,0,new Color(.055f,.076f,.14f,.97f)); panelTexture.Apply();
        }
        static void Fill(Rect rect,Color color)
        {
            Color old=GUI.color; GUI.color=color; GUI.DrawTexture(rect,Texture2D.whiteTexture); GUI.color=old;
        }
        void OnGUI()
        {
            if(!configured || ExternalOverlayOpen) return;
            EnsureStyles();
            Matrix4x4 matrix=GUI.matrix;
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
            Vector2 offset=new Vector2((Screen.width-1280*scale)*.5f,(Screen.height-720*scale)*.5f);
            GUI.matrix=Matrix4x4.TRS(offset,Quaternion.identity,new Vector3(scale,scale,1));
            GUI.DrawTexture(new Rect(0,0,1280,110),panelTexture);
            Fill(new Rect(0,109,1280,1),new Color(.24f,.32f,.45f,.65f));
            GUI.Label(new Rect(32,14,270,20),"COIN RALLY  /  2 PLAYERS",eyebrowStyle);
            GUI.Label(new Rect(30,35,510,40),"ふたりで コインラリー",titleStyle);
            GUI.Label(new Rect(32,76,510,24),"光るボールを返して、隠れたコインを見つけよう",smallStyle);
            GUI.Label(new Rect(602,16,170,24),$"ROUND {Round:00}",eyebrowStyle);
            GUI.Label(new Rect(596,40,178,42),$"{CollectedCount} / {CoinsPerRound}",scoreStyle);
            for(int i=0;i<CoinsPerRound;i++)
                Fill(new Rect(602+i*19,88,14,4),i<CollectedCount ? new Color(1,.8f,.32f) : new Color(.24f,.28f,.36f));
            GUI.Label(new Rect(846,16,174,24),"ふたりのコイン",eyebrowStyle);
            GUI.Label(new Rect(836,40,170,42),$"{TeamScore:000}",scoreStyle);
            if(GUI.Button(new Rect(1063,19,83,35),paused ? "再開" : "一時停止",buttonStyle)) SetPaused(!paused);
            if(GUI.Button(new Rect(1155,19,94,35),"設定",buttonStyle)) { settingsOpen=!settingsOpen; ApplySimulationState(); }
            if(GUI.Button(new Rect(1063,65,186,29),"はじめから",buttonStyle)) RestartGame();

            if(settingsOpen) DrawSettings();
            else if(!inputReady) DrawMessage("ふたりの準備を待っています","両手をセンサーに見せると再開します",new Color(.44f,.83f,.92f));
            else if(paused) DrawMessage("ひとやすみ","「再開」で続けられます",new Color(.76f,.82f,.95f));
            else if(clearRemaining>=0) DrawMessage("全部とれた！",$"ふたりで {TeamScore} コイン。次のアーチへ！",new Color(1,.8f,.34f));
            else if(serveRemaining>=0)
            {
                string name=servingSide<0 ? "左のプレイヤー" : "右のプレイヤー";
                GUI.Label(new Rect(350,125,580,32),$"{name}からスタート",new GUIStyle(centeredStyle) {fontSize=19,normal={textColor=servingSide<0 ? new Color(.36f,.91f,.96f) : new Color(1,.57f,.51f)}});
            }
            GUI.matrix=matrix;
        }

        void DrawMessage(string title,string detail,Color accent)
        {
            GUI.DrawTexture(new Rect(362,280,556,132),panelTexture);
            Fill(new Rect(362,280,4,132),accent);
            GUI.Label(new Rect(380,301,520,44),title,centeredStyle);
            GUI.Label(new Rect(380,355,520,31),detail,new GUIStyle(bodyStyle) {alignment=TextAnchor.MiddleCenter});
        }
        void DrawSettings()
        {
            Fill(new Rect(0,110,1280,610),new Color(.015f,.025f,.06f,.72f));
            GUI.DrawTexture(new Rect(380,164,520,440),panelTexture);
            GUI.Label(new Rect(414,186,420,40),"遊びやすさを調整",titleStyle);
            GUI.Label(new Rect(414,239,420,30),$"ネットの高さ   {NetHeight:F1}",bodyStyle);
            NetHeight=GUI.HorizontalSlider(new Rect(418,282,438,22),NetHeight,1f,3.1f);
            GUI.Label(new Rect(414,307,440,25),"低いほど、ふたりでラリーをつなげやすい",smallStyle);
            GUI.Label(new Rect(414,355,420,30),$"ボールの速さ   {BallSpeed:F1}",bodyStyle);
            BallSpeed=GUI.HorizontalSlider(new Rect(418,398,438,22),BallSpeed,6.5f,10.5f);
            GUI.Label(new Rect(414,423,440,25),"手の幅を狭めると強く、傾けると左右へ",smallStyle);
            GUI.Label(new Rect(414,466,270,28),"ボールの光",bodyStyle);
            if(GUI.Button(new Rect(746,461,120,34),BallLightEnabled ? "ON" : "OFF",buttonStyle)) BallLightEnabled=!BallLightEnabled;
            GUI.Label(new Rect(414,500,452,25),"光が届く範囲だけ、コインが見えて取れます",smallStyle);
            if(GUI.Button(new Rect(414,544,452,36),"ゲームに戻る",buttonStyle)) { settingsOpen=false; ApplySimulationState(); }
        }

        void OnDestroy()
        {
            if(ball != null && ball.CooperativeGame==this) ball.CooperativeGame=null;
            Release(arenaRoot); Release(coinSprite); Release(whiteSprite); Release(haloSprite); Release(lightSprite);
            Release(coinTexture); Release(whiteTexture); Release(haloTexture); Release(lightTexture); Release(panelTexture);
            Release(wallMaterial);
        }
        static void Release(Object obj)
        {
            if(obj==null) return;
            if(Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }
    }
}
