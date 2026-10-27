using System.Collections.Generic;
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
    // ExecuteAlways keeps lifecycle cleanup (OnDestroy) reliable when the component is
    // destroyed or reloaded in edit mode; all frame callbacks stay play-mode only.
    [ExecuteAlways]
    public sealed partial class CooperativeCoinGame : MonoBehaviour
    {
        public const int CoinsPerRound = 9;
        const float MaximumBallSpeedFactor = 1.37f;
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
        Sprite coinSprite, whiteSprite, haloSprite, lightSprite, ballCoreSprite, originalBallSprite;
        Sprite configuredStarSprite, configuredBallSprite;
        readonly List<Sprite> ownedVisualWrappers = new List<Sprite>();
        RallyVisualConfig visualConfig;
        Texture2D coinTexture, whiteTexture, haloTexture, lightTexture, ballCoreTexture;
        PhysicsMaterial2D wallMaterial;
        GUIStyle titleStyle, bodyStyle, scoreStyle, goalStyle, buttonStyle, centeredStyle, smallStyle;
        GUIStyle leftScoreStyle, rightScoreStyle, matchHintStyle, pointStyle, effectStyle, winStyle, collectionCountStyle;
        Texture2D panelTexture;
        Vector2 center, previous;
        float halfWidth, halfHeight, serveRemaining=-1, serveDuration, clearRemaining=-1, animationTime, presentationDelta;
        int servingSide=-1;
        int lastPointWinner;
        bool configured, paused, inputReady=true, settingsOpen;

        /// <summary>Optional presentation assets. Supplied before Configure or via Configure.</summary>
        public RallyVisualConfig VisualConfig { get => visualConfig; set => visualConfig = value; }
        public bool UsesConfiguredBallSprite => configuredBallSprite != null;
        public bool UsesConfiguredStarSprite => configuredStarSprite != null;
        public Sprite BallArtworkSprite => ballView != null ? ballView.sprite : null;
        public Sprite StarArtworkSprite => coinSprite;
        public int TeamScore => progress.TeamScore;
        public int Round => progress.Round;
        public int CollectedCount => progress.CollectedCount;
        public int Misses => progress.Misses;
        public bool IsPaused => paused;
        public bool InputReady => inputReady;
        public bool SettingsOpen => settingsOpen;
        public bool ExternalOverlayOpen { get; set; }
        public string InputWaitTitle { get; set; } = "準備中";
        public string InputWaitDetail { get; set; } = "両手を見せてください";
        public bool WaitingToServe => serveRemaining >= 0;
        public bool CanAdvanceBall => configured && isActiveAndEnabled && inputReady && !paused && !settingsOpen && !ExternalOverlayOpen && serveRemaining<0 && clearRemaining<0 && (!IsVersus || !versus.Complete);
        public bool RoundComplete => progress.Complete;
        public float ArenaFloor => center.y-halfHeight+0.6f;
        public float MatchFloor => ArenaFloor+1f;
        public float HalfWidth => halfWidth;
        public BoxCollider2D NetCollider => net;
        public bool BallLightEnabled
        {
            get => ballLightEnabled;
            set
            {
                if(ballLightEnabled==value) return;
                ballLightEnabled=value;
                if(!value) ResetStarHint();
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
            set { ballSpeed=Mathf.Clamp(value,6.5f,10.5f); if(ball != null) ball.maximumSpeed=ballSpeed*MaximumBallSpeedFactor; }
        }

        public void Configure(AirHockeyBall gameBall, Camera view, Font japaneseFont = null,
            RallyVisualConfig presentationConfig = null)
        {
            if(configured) return;
            if(presentationConfig != null) visualConfig=presentationConfig;
            if(visualConfig == null) visualConfig=RallyVisualConfig.LoadDefault();
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
            ball.maximumSpeed=ballSpeed*MaximumBallSpeedFactor;
            BuildArtwork(); BuildArena(); BuildGardenPresentation(); BuildCoins(); BuildBallLight(); BuildStarHintArtwork(); BuildVersusArtwork(); BuildBattlePresentation();
            configured=true;
            RefreshNet(); PlaceCoins(); BeginServe(1.4f);
        }

        void BuildArtwork()
        {
            whiteTexture=new Texture2D(1,1,TextureFormat.RGBA32,false);
            whiteTexture.SetPixel(0,0,Color.white); whiteTexture.Apply();
            whiteSprite=Sprite.Create(whiteTexture,new Rect(0,0,1,1),Vector2.one*0.5f,1);
            coinTexture=RallyStarArtwork.Create();
            BuildBallCoreArtwork();
            haloTexture=new Texture2D(64,64,TextureFormat.RGBA32,false);
            lightTexture=new Texture2D(128,128,TextureFormat.RGBA32,false) { wrapMode=TextureWrapMode.Clamp };
            for(int y=0;y<64;y++) for(int x=0;x<64;x++)
            {
                float dx=x-31.5f, dy=y-31.5f;
                float distance=Mathf.Sqrt(dx*dx+dy*dy);
                haloTexture.SetPixel(x,y,new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-distance/32),2)));
            }
            haloTexture.Apply();
            for(int y=0;y<128;y++) for(int x=0;x<128;x++)
            {
                float distance=Vector2.Distance(new Vector2(x,y),new Vector2(63.5f,63.5f))/64;
                // Fall off toward the edge without drawing outside the reveal mask.
                float radiance=Mathf.Pow(Mathf.Clamp01(1-distance),2)*RevealAmount(distance,1);
                lightTexture.SetPixel(x,y,new Color(1,1,1,radiance));
            }
            lightTexture.Apply();
            configuredStarSprite=CreateConfiguredSprite(visualConfig!=null?visualConfig.star:null,.38f,"Configured star");
            coinSprite=configuredStarSprite!=null ? configuredStarSprite :
                Sprite.Create(coinTexture,new Rect(0,0,coinTexture.width,coinTexture.height),Vector2.one*.5f,coinTexture.width/.38f);
            haloSprite=Sprite.Create(haloTexture,new Rect(0,0,64,64),Vector2.one*.5f,64);
            lightSprite=Sprite.Create(lightTexture,new Rect(0,0,128,128),Vector2.one*.5f,128);
        }

        void BuildBallCoreArtwork()
        {
            configuredBallSprite=CreateConfiguredSprite(visualConfig!=null?visualConfig.ball:null,.44f,
                "Configured rally ball");
            if(configuredBallSprite!=null)
            {
                ballCoreSprite=configuredBallSprite;
                originalBallSprite=ballView.sprite;ballView.sprite=ballCoreSprite;
                return;
            }
            const int size=96;
            ballCoreTexture=new Texture2D(size,size,TextureFormat.RGBA32,false) {
                name="Rally pearl core",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear
            };
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float radius=Vector2.Distance(new Vector2(x+.5f,y+.5f),Vector2.one*(size*.5f))/(size*.46f);
                float shade=Mathf.Lerp(1,.94f,Mathf.SmoothStep(0,1,radius));
                float alpha=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.975f,1,radius));
                ballCoreTexture.SetPixel(x,y,new Color(shade,shade,shade,alpha));
            }
            ballCoreTexture.Apply();
            // The visible rim keeps the existing .22 world-unit collision radius.
            ballCoreSprite=Sprite.Create(ballCoreTexture,new Rect(0,0,size,size),Vector2.one*.5f,size/(.44f/.92f));
            originalBallSprite=ballView.sprite;ballView.sprite=ballCoreSprite;
        }

        Sprite CreateConfiguredSprite(Sprite source,float maxWorldSize,string wrapperName,bool track=false)
        {
            var wrapper=RallySceneryArtwork.CreateNormalizedSprite(source,maxWorldSize,wrapperName);
            if(wrapper!=null&&track)ownedVisualWrappers.Add(wrapper);
            return wrapper;
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
                new Vector2(halfWidth,halfHeight*1.62f),new Color(.08f,.16f,.29f,.10f),-10);
            Rectangle("Right court wash",center+new Vector2(halfWidth*.5f,-.35f),
                new Vector2(halfWidth,halfHeight*1.62f),new Color(.20f,.12f,.29f,.10f),-10);
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
            if(cyber!=null)cyber.SetNet(ArenaFloor+netHeight,ArenaFloor);
        }

        void BuildCoins()
        {
            for(int i=0;i<CoinsPerRound;i++)
            {
                var go=CreateChild("Shared coin "+(i+1),arenaRoot.transform);
                coins[i]=go.AddComponent<SpriteRenderer>(); coins[i].sprite=coinSprite; coins[i].sortingOrder=7;
                var glow=CreateChild("Warm halo",go.transform);
                glows[i]=glow.AddComponent<SpriteRenderer>(); glows[i].sprite=haloSprite;
                glows[i].sortingOrder=6; glows[i].color=new Color(1,.91f,.67f,.14f);
                glow.transform.localScale=Vector3.one*.62f;
            }
        }

        void BuildBallLight()
        {
            ballLight=CreateChild("Ball light",arenaRoot.transform).AddComponent<SpriteRenderer>();
            ballLight.sprite=lightSprite; ballLight.sortingOrder=4;
            ballLight.color=new Color(1,.94f,.74f,.014f);
            ballCorona=CreateChild("Ball corona",arenaRoot.transform).AddComponent<SpriteRenderer>();
            ballCorona.sprite=haloSprite; ballCorona.sortingOrder=8;
            ballCorona.color=IsVersus ? new Color(.73f,.88f,1,.30f) : new Color(1,.96f,.85f,.38f);
            ballCorona.transform.localScale=Vector3.one*.82f;
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
            ResetStarHint();
            if(garden!=null)garden.ResetRoundPresentation();
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
            ball.CancelBarrierImpact();
            ResetVersusRally();
            servingSide=IsVersus ? versus.NextServeSide : progress.TakeServeSide();
            serveRemaining=serveDuration=delay;
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
            if(ball != null) ball.SetGameplaySimulation(CanAdvanceBall);
            SyncBattleSuspension();
        }
        public void RestartGame()
        {
            if(!configured) return;
            progress.Reset(); versus.Reset(); lastPointWinner=0; clearRemaining=-1; paused=false;
            ResetPickupsForMatch(); ClearLightPhase();
            ResetBattlePresentation();
            PlaceCoins(); BeginServe(1.2f);
        }

        void Update()
        {
            if(!Application.isPlaying) return;
            if(!configured) return;
            // Detect a resume before stepping any timer so a hidden phase replays
            // its visible warning instead of spending hidden time on the first frame.
            SyncSpellSuspension();
            AdvanceBattlePresentation(Time.deltaTime);
            // The garden and old pickup animation share one suspension-aware clock.
            // It still ticks through serve/clear waits, but never through a blocker.
            bool presentationRunning=inputReady&&!paused&&!settingsOpen&&!ExternalOverlayOpen&&isActiveAndEnabled&&!IsVersus;
            presentationDelta=presentationRunning ? Time.deltaTime : 0f;
            if(garden!=null)garden.StepPresentation(presentationDelta,progress.Complete);
            if(!inputReady || paused || settingsOpen || ExternalOverlayOpen || (IsVersus && versus.Complete)) return;
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
                    lastPointWinner=0;
                    ball.Body.linearVelocity=new Vector2(-servingSide*.25f,-.5f);
                    ApplySimulationState();
                }
                return;
            }
            AdvanceSpellTimers(Time.deltaTime);
            Vector2 current=ball.Body.position;
            if(IsVersus)
            {
                if(!ball.IsInBarrierImpact && TryAwardVersusPoint(previous,current)) return;
                float ballRadius=ballCollider.radius*Mathf.Max(Mathf.Abs(ball.transform.lossyScale.x),Mathf.Abs(ball.transform.lossyScale.y));
                UpdateVersus(Time.deltaTime,previous,current,ballRadius+.30f);
                previous=current;
                return;
            }
            if(current.y<ArenaFloor-.8f)
            {
                progress.RegisterMiss(); BeginServe(1.2f);
                return;
            }
            float radius=ballCollider.bounds.extents.x+.27f;
            for(int i=0;i<CoinsPerRound;i++)
            {
                if(ballLightEnabled && !progress.IsCollected(i) && SweptHit(previous,current,coinPositions[i],radius) && progress.Collect(i))
                {
                    pickupAges[i]=0;
                    ResetStarHint();
                    // Collect() is the single authority: one notification per real pickup.
                    if(garden!=null)garden.NotifyStarCollected(i,coinPositions[i]);
                }
            }
            AdvanceStarHint(Time.deltaTime);
            previous=current;
            if(progress.Complete)
            {
                clearRemaining=2.0f; ApplySimulationState();
            }
        }

        void LateUpdate()
        {
            if(!Application.isPlaying) return;
            if(!configured) return;
            animationTime+=presentationDelta;
            for(int i=0;i<CoinsPerRound;i++)
            {
                if(pickupAges[i]>=0)
                {
                    pickupAges[i]+=presentationDelta;
                    float fade=Mathf.Clamp01(1-pickupAges[i]/.35f);
                    coins[i].transform.localScale=Vector3.one*(1+(1-fade)*.8f);
                }
                else
                {
                    float pulse=.96f+.04f*Mathf.Sin(animationTime*2.5f+i*.65f);
                    coins[i].transform.localScale=Vector3.one*pulse;
                }
            }
            SyncSpellSuspension();
            RefreshVersusArtwork();
            RefreshLighting();
        }

        // A short fade inside the same radius makes the illuminated edge legible.
        // This bounded mask is shared by the visible field and star visibility.
        public static float RevealAmount(float distance,float radius)
        {
            if(radius<=0) return 0;
            return 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(radius*.80f,radius,distance));
        }

        public float CoinVisibility(int index)
        {
            if(!configured || IsVersus || !ballLightEnabled || index<0 || index>=CoinsPerRound) return 0;
            return RevealAmount(Vector2.Distance(coinPositions[index],ball.transform.position),ballLightRadius);
        }

        void RefreshLighting()
        {
            CheckLightSafetyForRendering();
            bool ballVisible=BallPresentationVisible;
            Color gold=visualConfig!=null?visualConfig.ballGold:new Color(.95f,.79f,.46f);
            ballLight.color=new Color(gold.r,gold.g,gold.b,.014f);
            ballCorona.color=new Color(gold.r,gold.g,gold.b,IsVersus?.30f:.38f);
            ballLight.transform.position=ball.transform.position;
            ballLight.transform.localScale=Vector3.one*(ballLightRadius*2);
            ballCorona.transform.position=ball.transform.position;
            ballLight.enabled=ballLightEnabled && ballVisible && !IsVersus;
            ballCorona.enabled=ballLightEnabled && ballVisible;
            for(int i=0;i<CoinsPerRound;i++)
            {
                float fade=pickupAges[i]>=0 ? Mathf.Clamp01(1-pickupAges[i]/.35f) : 1;
                float alpha=CoinVisibility(i)*fade;
                coins[i].enabled=glows[i].enabled=alpha>.001f;
                coins[i].color=new Color(1,1,1,alpha);
                glows[i].color=new Color(1,.91f,.67f,alpha*(pickupAges[i]>=0 ? .26f : .14f));
            }
            float coreAlpha=serveRemaining>=0 ? .75f+.25f*Mathf.Pow(Mathf.Sin(animationTime*5),2) : 1;
            Color ivory=visualConfig!=null?visualConfig.ballIvory:new Color(1,.995f,.96f);
            ivory.a=coreAlpha;
            ballView.color=ivory;
            // Presentation only: the ball, its light, and attached spell artwork vanish
            // together while Light is hidden. Physics never changes.
            ballView.enabled=ballVisible;
            if(ballSpellRoot!=null) ballSpellRoot.SetActive(ballVisible && IsVersus && ActiveSpell!=RallySpell.None);
            if(lightPhase==RallyLightPhase.Warning && ballVisible && ballView.enabled) lightWarningPresented=true;
            RefreshStarHint();
        }

        public static bool SweptHit(Vector2 from,Vector2 to,Vector2 coin,float radius)
        {
            Vector2 delta=to-from;
            float t=delta.sqrMagnitude>0 ? Mathf.Clamp01(Vector2.Dot(coin-from,delta)/delta.sqrMagnitude) : 0;
            return (coin-(from+delta*t)).sqrMagnitude<=radius*radius;
        }

        public Vector2 BarrierBounce(Vector2 velocity,Vector2 normal,float softness,Vector2 position,float handBoost=0)
        {
            return CooperativeReflection(velocity,normal,softness,position.x-center.x,ballSpeed,handBoost);
        }
        public Vector2 PlayerBarrierBounce(Vector2 velocity,Vector2 normal,float softness,Vector2 position,int playerSide)
        {
            RegisterBarrierHit(playerSide);
            return BarrierBounce(velocity,normal,softness,position);
        }

        internal bool TryScoreFloorBeforeBarrier(Vector2 contactPosition)
        {
            // A low barrier cannot rescue a ball that has already touched the floor.
            // Check before compression, which may legitimately dip below the line.
            return IsVersus && CanAdvanceBall && TryAwardVersusPoint(previous,contactPosition);
        }

        bool TryAwardVersusPoint(Vector2 from,Vector2 to)
        {
            float radius=ballCollider.radius*Mathf.Max(Mathf.Abs(ball.transform.lossyScale.x),Mathf.Abs(ball.transform.lossyScale.y));
            float floor=MatchFloor+radius;
            if(to.y>floor) return false;
            float t=from.y>floor && from.y>to.y ? Mathf.Clamp01((from.y-floor)/(from.y-to.y)) : 1;
            float crossingX=Mathf.Lerp(from.x,to.x,t)-center.x;
            int losingSide=Mathf.Abs(crossingX)<.001f ? servingSide : crossingX<0 ? -1 : 1;
            if(!versus.AwardPoint(losingSide)) return false;
            ClearLightPhase();
            lastPointWinner=-losingSide;
            CelebratePoint(new Vector2(center.x+crossingX,MatchFloor));
            if(versus.Complete)
            {
                ball.CancelBarrierImpact();
                ball.Body.linearVelocity=Vector2.zero;
                ResetVersusRally();
                ApplySimulationState();
            }
            else BeginServe(1.6f);
            return true;
        }
        public static Vector2 CooperativeReflection(Vector2 velocity,Vector2 normal,float softness,float ballX,float speed,float handBoost=0)
        {
            normal=normal.sqrMagnitude>.0001f ? normal.normalized : Vector2.up;
            float width=Mathf.Clamp01(softness);
            // No court-side steering: preserve incidence and the tangential direction.
            // If a held barrier has rotated past the incoming path, let the ball leave.
            Vector2 reflected=velocity-2*Mathf.Min(0,Vector2.Dot(velocity,normal))*normal;
            Vector2 direction=reflected.sqrMagnitude>.0001f ? reflected.normalized : normal;
            // A low-speed floor keeps a stationary barrier playable. Above that floor,
            // incoming speed matters; wide hands still absorb more of the return.
            float reboundSpeed=Mathf.Max(velocity.magnitude,speed*.85f)*Mathf.Lerp(1.14f,.58f,width*width);
            reboundSpeed=Mathf.Min(reboundSpeed,speed*1.14f);
            return direction*reboundSpeed*(1+.2f*Mathf.Clamp01(handBoost));
        }

        void EnsureStyles()
        {
            if(titleStyle != null) return;
            titleStyle=new GUIStyle(GUI.skin.label) { font=GuiFont,fontSize=25,fontStyle=FontStyle.Normal,normal={textColor=new Color(.95f,.95f,.86f)} };
            bodyStyle=new GUIStyle(titleStyle) { fontSize=17,fontStyle=FontStyle.Normal };
            smallStyle=new GUIStyle(bodyStyle) { fontSize=13,normal={textColor=new Color(.65f,.72f,.83f)} };
            scoreStyle=new GUIStyle(titleStyle) { fontSize=52,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(0,0,0,0),normal={textColor=new Color(1,.95f,.78f)} };
            goalStyle=new GUIStyle(bodyStyle) { fontSize=26,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(0,0,0,0),normal={textColor=new Color(.64f,.69f,.81f)} };
            collectionCountStyle=new GUIStyle(scoreStyle) { fontSize=48,wordWrap=false,clipping=TextClipping.Clip,contentOffset=Vector2.zero };
            goalStyle.wordWrap=false;
            leftScoreStyle=new GUIStyle(scoreStyle) { alignment=TextAnchor.MiddleCenter,normal={textColor=PlayerColor(-1)} };
            rightScoreStyle=new GUIStyle(scoreStyle) { alignment=TextAnchor.MiddleCenter,normal={textColor=PlayerColor(1)} };
            matchHintStyle=new GUIStyle(smallStyle) { alignment=TextAnchor.MiddleCenter };
            centeredStyle=new GUIStyle(titleStyle) { alignment=TextAnchor.MiddleCenter,fontSize=24 };
            pointStyle=new GUIStyle(centeredStyle) { fontSize=21 };
            effectStyle=new GUIStyle(matchHintStyle) { fontSize=15,normal={textColor=new Color(.88f,.74f,1)} };
            winStyle=new GUIStyle(centeredStyle) { fontSize=36 };
            panelTexture=new Texture2D(1,1); panelTexture.SetPixel(0,0,new Color(.055f,.076f,.14f,.97f)); panelTexture.Apply();
            BuildControlStyles();
        }
        Color PlayerColor(int side) => side<0
            ? (visualConfig!=null?visualConfig.playerJade:new Color(.64f,.94f,.80f))
            : (visualConfig!=null?visualConfig.playerGold:new Color(.94f,.76f,.49f));

        void DrawCollectionHud()
        {
            // Give every glyph a full line box and a little horizontal slack. A label
            // exactly as wide as CalcSize can wrap after GUI scaling and rise above the card.
            string count=CollectedCount.ToString(), goal=$"/ {CoinsPerRound}";
            Vector2 countSize=collectionCountStyle.CalcSize(new GUIContent(count));
            float countWidth=Mathf.Max(48,countSize.x+12);
            float goalWidth=goalStyle.CalcSize(new GUIContent(goal)).x+12;
            float rowHeight=Mathf.Max(64,countSize.y+8);
            float cardHeight=rowHeight+40;
            var card=new Rect(486,32,308,cardHeight);
            GUI.BeginGroup(card);
            float start=(card.width-(32+12+countWidth+8+goalWidth))*.5f;
            GUI.DrawTexture(new Rect(start,10+(rowHeight-32)*.5f,32,32),coinTexture);
            DrawHudLabel(new Rect(start+44,10,countWidth,rowHeight),count,collectionCountStyle);
            DrawHudLabel(new Rect(start+52+countWidth,10,goalWidth,rowHeight),goal,goalStyle);
            for(int i=0;i<CoinsPerRound;i++)
            {
                Color old=GUI.color;
                GUI.color=i<CollectedCount ? new Color(1,.91f,.64f) : new Color(.56f,.65f,.58f,.85f);
                GUI.DrawTexture(new Rect(93+i*14,rowHeight+12,10,10),haloTexture);
                GUI.color=old;
            }
            GUI.EndGroup();
        }

        void DrawVersusHud()
        {
            DrawPlayerScore(508,-1,versus.LeftScore,leftScoreStyle);
            DrawPlayerScore(672,1,versus.RightScore,rightScoreStyle);
            Fill(new Rect(631,78,18,1),new Color(.49f,.59f,.68f,.65f));
            DrawBattlePoint();
        }

        void DrawPlayerScore(float x,int side,int score,GUIStyle style)
        {
            Color color=PlayerColor(side);
            float age=battlePointAge;
            bool scored=lastPointWinner==side && age<1.15f;
            float pop=scored ? Mathf.Sin(Mathf.Clamp01(age/.42f)*Mathf.PI) : 0;
            style.fontSize=Mathf.RoundToInt(52+pop*26);
            DrawHudLabel(new Rect(x-8,32-pop*4,116,92),score.ToString(),style);
            for(int i=0;i<RallyVersusState.PointsToWin;i++)
            {
                Color pip=color; pip.a=i<score ? 1 : .35f;
                float size=scored && i==score-1 ? 6+pop*5 : 6;
                Fill(new Rect(x+35+i*14-size*.5f,129-size*.5f,size,size),pip);
            }
            if(ActiveSpell!=RallySpell.None && SpellOwner==side)
                DrawSpellIcon(new Vector2(x+84,170),ActiveSpell);
        }

        static void DrawHudLabel(Rect rect,string text,GUIStyle style)
        {
            Color ink=style.normal.textColor;
            style.normal.textColor=new Color(.008f,.014f,.025f,.90f);
            GUI.Label(new Rect(rect.x+2,rect.y+2,rect.width,rect.height),text,style);
            style.normal.textColor=ink;
            GUI.Label(rect,text,style);
        }

        static void Stroke(Vector2 from,Vector2 to,float thickness,Color color)
        {
            Vector2 delta=to-from;
            Matrix4x4 old=GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,from);
            Fill(new Rect(from.x,from.y-thickness*.5f,delta.magnitude,thickness),color);
            GUI.matrix=old;
        }

        static void DrawSpellIcon(Vector2 center,RallySpell spell)
        {
            Color color=SpellColor(spell);
            if(spell==RallySpell.Fireball)
            {
                Stroke(center+new Vector2(-7,6),center+new Vector2(1,-7),2,color);
                Stroke(center+new Vector2(7,6),center+new Vector2(-1,-7),2,color);
                Stroke(center+new Vector2(-5,-6),center+new Vector2(5,-6),1.6f,color);
            }
            else if(spell==RallySpell.Timer)
            {
                const int segments=10;
                Vector2 previous=center+new Vector2(0,7);
                for(int i=1;i<=segments;i++)
                {
                    float angle=i*Mathf.PI*2/segments;
                    Vector2 point=center+new Vector2(Mathf.Sin(angle),Mathf.Cos(angle))*7;
                    Stroke(previous,point,1.4f,color);
                    previous=point;
                }
                Stroke(center,center+new Vector2(0,4),1.4f,color);
                Stroke(center,center+new Vector2(3,0),1.4f,color);
            }
            else
            {
                Stroke(center+new Vector2(0,8),center+new Vector2(0,4),1.4f,color);
                Stroke(center+new Vector2(-5,4),center+new Vector2(-6,-5),1.4f,color);
                Stroke(center+new Vector2(5,4),center+new Vector2(6,-5),1.4f,color);
                Stroke(center+new Vector2(-6,-5),center+new Vector2(6,-5),1.4f,color);
                Fill(new Rect(center.x-1.5f,center.y-1.5f,3,3),new Color(1,.88f,1));
            }
        }

        void DrawServeCue(float scale,Vector2 offset)
        {
            Vector3 screen=cameraView.WorldToScreenPoint(ball.transform.position);
            Vector2 point=(new Vector2(screen.x,Screen.height-screen.y)-offset)/scale;
            float progress=1-Mathf.Clamp01(serveRemaining/Mathf.Max(.01f,serveDuration));
            for(int i=0;i<3;i++)
            {
                Color color=PlayerColor(servingSide);
                color.a=Mathf.Lerp(.2f,1,Mathf.Clamp01(progress*3-i));
                Fill(new Rect(point.x-21+i*16,point.y+35,10,5),color);
            }
        }

        void DrawVictory()
        {
            DrawBattleVictory();
        }

        void DrawRoundClear()
        {
            float bloom=garden!=null?garden.BloomProgress:0;
            Color gold=visualConfig!=null?visualConfig.gardenGold:new Color(.94f,.76f,.49f);
            gold.a=.82f;
            float pulse=1+Mathf.Sin(Mathf.Clamp01(bloom)*Mathf.PI)*.10f;
            Matrix4x4 old=GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one*pulse,new Vector2(640,330));
            winStyle.fontSize=58;winStyle.fontStyle=FontStyle.Normal;winStyle.alignment=TextAnchor.MiddleCenter;
            winStyle.normal.textColor=gold;
            GUI.Label(new Rect(540,292,200,78),"+"+CoinsPerRound,winStyle);
            GUI.matrix=old;
            GUI.Label(new Rect(540,370,200,24),$"累計 {TeamScore}",matchHintStyle);

            // Peripheral runes and restrained gold sparks replace the old opaque card.
            for(int side=-1;side<=1;side+=2)
            {
                float x=640+side*214;
                Color rune=gold;rune.a=.20f+bloom*.38f;
                Vector2 previous=new Vector2(x+34,330);
                for(int i=1;i<=14;i++)
                {
                    float angle=i*Mathf.PI*2/14;
                    Vector2 point=new Vector2(x+Mathf.Cos(angle)*34,330+Mathf.Sin(angle)*34);
                    Stroke(previous,point,1.4f,rune);previous=point;
                }
                Fill(new Rect(x-2,330-2,4,4),rune);
            }
            for(int i=0;i<10;i++)
            {
                float angle=i*Mathf.PI*2/10+.2f;
                float inner=64+bloom*18,outer=inner+10;
                Color spark=gold;spark.a=.12f+bloom*.35f;
                Stroke(new Vector2(640+Mathf.Cos(angle)*inner,330+Mathf.Sin(angle)*inner),
                    new Vector2(640+Mathf.Cos(angle)*outer,330+Mathf.Sin(angle)*outer),2,spark);
            }
        }
        static void Fill(Rect rect,Color color)
        {
            Color old=GUI.color; GUI.color=color; GUI.DrawTexture(rect,Texture2D.whiteTexture); GUI.color=old;
        }
        void OnGUI()
        {
            if(!Application.isPlaying) return;
            if(!configured || ExternalOverlayOpen) return;
            EnsureStyles();
            Matrix4x4 matrix=GUI.matrix;
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
            Vector2 offset=new Vector2((Screen.width-1280*scale)*.5f,(Screen.height-720*scale)*.5f);
            GUI.matrix=Matrix4x4.TRS(offset,Quaternion.identity,new Vector3(scale,scale,1));
            if(IsVersus) DrawVersusHud();
            else DrawCollectionHud();
            DrawBottomControls();

            if(settingsOpen) DrawSettings();
            else if(!inputReady) DrawMessage(InputWaitTitle,InputWaitDetail,new Color(.44f,.83f,.92f));
            else if(paused) DrawPauseMenu();
            else if(IsVersus && versus.Complete)
            {
                DrawVictory();
                if(GUI.Button(new Rect(1050,42,186,40),"もう一度",buttonStyle))RestartGame();
            }
            else if(clearRemaining>=0) DrawRoundClear();
            else if(serveRemaining>=0) DrawServeCue(scale,offset);
            GUI.matrix=matrix;
        }

        void DrawMessage(string title,string detail,Color accent)
        {
            GUI.DrawTexture(new Rect(460,300,360,98),panelTexture);
            Fill(new Rect(460,300,3,98),accent);
            GUI.Label(new Rect(480,313,320,40),title,centeredStyle);
            if(!string.IsNullOrEmpty(detail)) GUI.Label(new Rect(480,361,320,25),detail,matchHintStyle);
        }
        void DrawSettings()
        {
            Fill(new Rect(0,0,1280,720),IsVersus ? new Color(.01f,.02f,.04f,.82f) : new Color(.015f,.035f,.025f,.82f));
            GUI.DrawTexture(new Rect(380,64,520,584),panelTexture);
            GUI.Label(new Rect(414,82,420,40),"設定",titleStyle);
            GUI.enabled=Mode!=RallyMode.StarCollection;
            if(GUI.Button(new Rect(414,136,216,40),"星あつめ",buttonStyle)) { SetMode(RallyMode.StarCollection);settingsOpen=true;ApplySimulationState(); }
            GUI.enabled=Mode!=RallyMode.Versus;
            if(GUI.Button(new Rect(646,136,216,40),"対戦 · 3点先取",buttonStyle)) { SetMode(RallyMode.Versus);settingsOpen=true;ApplySimulationState(); }
            GUI.enabled=true;
            GUI.Label(new Rect(414,204,420,30),$"ネットの高さ   {NetHeight:F1}",bodyStyle);
            NetHeight=GUI.HorizontalSlider(new Rect(418,248,438,22),NetHeight,1f,3.1f,sliderStyle,sliderThumbStyle);
            GUI.Label(new Rect(414,285,420,30),$"ボールの速さ   {BallSpeed:F1}",bodyStyle);
            BallSpeed=GUI.HorizontalSlider(new Rect(418,329,438,22),BallSpeed,6.5f,10.5f,sliderStyle,sliderThumbStyle);
            GUI.Label(new Rect(414,370,270,28),"ボールの光",bodyStyle);
            if(GUI.Button(new Rect(746,365,120,34),BallLightEnabled ? "ON" : "OFF",buttonStyle)) BallLightEnabled=!BallLightEnabled;
            Fill(new Rect(414,426,452,1),new Color(.32f,.43f,.35f,.5f));
            var app=GetComponent<CooperativeRallyApp>();
            if(app!=null)app.DrawInputControls(new Rect(414,446,452,100));
            if(GUI.Button(new Rect(414,584,216,38),"はじめから",quietButtonStyle)) { RestartGame();settingsOpen=false;ApplySimulationState(); }
            if(GUI.Button(new Rect(646,584,216,38),"戻る",buttonStyle)) { settingsOpen=false; ApplySimulationState(); }
        }

        void OnEnable() { if(configured) ApplySimulationState(); }
        void OnDisable()
        {
            presentationDelta=0f;
            SyncBattleSuspension();
            SyncSpellSuspension();
            if(garden!=null)garden.StepPresentation(0,progress.Complete);
            if(ball==null) return;
            ball.CancelBarrierImpact();
            ball.SetGameplaySimulation(false);
            // A disabled component no longer runs LateUpdate, so suspension must restore
            // the ball's presentation here instead of leaving a hidden core, corona, and
            // attached spell glyph offscreen until OnEnable. Spell clocks are untouched;
            // enabling again replays the warning via SyncSpellSuspension.
            if(configured && ballView!=null) { RefreshVersusArtwork(); RefreshLighting(); }
        }
        void OnDestroy()
        {
            if(ball != null) ball.CancelBarrierImpact();
            if(ball != null && ball.CooperativeGame==this) ball.CooperativeGame=null;
            CleanupVersusArtwork();
            CleanupGardenPresentation();
            if(ballView!=null&&ballView.sprite==ballCoreSprite)ballView.sprite=originalBallSprite;
            Release(ballCoreSprite); Release(ballCoreTexture);
            for(int i=ownedVisualWrappers.Count-1;i>=0;i--)Release(ownedVisualWrappers[i]);
            ownedVisualWrappers.Clear();
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
