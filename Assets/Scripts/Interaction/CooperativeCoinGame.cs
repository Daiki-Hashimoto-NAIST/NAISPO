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
    public sealed partial class CooperativeCoinGame : MonoBehaviour
    {
        public const int CoinsPerRound = 9;
        const float MaximumBallSpeedFactor = 1.18f;
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
        GUIStyle titleStyle, bodyStyle, scoreStyle, goalStyle, buttonStyle, centeredStyle, smallStyle;
        GUIStyle leftScoreStyle, rightScoreStyle, matchHintStyle, pointStyle, effectStyle, winStyle, collectionCountStyle;
        Texture2D panelTexture;
        Vector2 center, previous;
        float halfWidth, halfHeight, serveRemaining=-1, serveDuration, clearRemaining=-1, animationTime;
        int servingSide=-1;
        int lastPointWinner;
        bool configured, paused, inputReady=true, settingsOpen;

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
            ball.maximumSpeed=ballSpeed*MaximumBallSpeedFactor;
            BuildArtwork(); BuildArena(); BuildCoins(); BuildBallLight(); BuildVersusArtwork();
            configured=true;
            RefreshNet(); PlaceCoins(); BeginServe(1.4f);
        }

        void BuildArtwork()
        {
            whiteTexture=new Texture2D(1,1,TextureFormat.RGBA32,false);
            whiteTexture.SetPixel(0,0,Color.white); whiteTexture.Apply();
            whiteSprite=Sprite.Create(whiteTexture,new Rect(0,0,1,1),Vector2.one*0.5f,1);
            coinTexture=RallyStarArtwork.Create();
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
                lightTexture.SetPixel(x,y,new Color(1,1,1,RevealAmount(distance,1)));
            }
            lightTexture.Apply();
            coinSprite=Sprite.Create(coinTexture,new Rect(0,0,coinTexture.width,coinTexture.height),Vector2.one*.5f,coinTexture.width/.64f);
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
        }
        public void RestartGame()
        {
            if(!configured) return;
            progress.Reset(); versus.Reset(); lastPointWinner=0; clearRemaining=-1; paused=false;
            PlaceCoins(); BeginServe(1.2f);
        }

        void Update()
        {
            if(!configured) return;
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
            RefreshVersusArtwork();
        }

        // Match the original light-coin game's full inner light and soft outer edge.
        public static float RevealAmount(float distance,float radius)
        {
            if(radius<=0) return 0;
            return LightCoinGame.RevealAmount(distance,radius);
        }

        public float CoinVisibility(int index)
        {
            if(!configured || IsVersus || !ballLightEnabled || index<0 || index>=CoinsPerRound) return 0;
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
        public Vector2 PlayerBarrierBounce(Vector2 velocity,Vector2 normal,float softness,Vector2 position,int playerSide)
        {
            float strength=IsVersus && CanAdvanceBall ? versus.RegisterReturn(playerSide) : 1;
            return BarrierBounce(velocity,normal,softness,position)*strength;
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
            lastPointWinner=-losingSide;
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
        public static Vector2 CooperativeReflection(Vector2 velocity,Vector2 normal,float softness,float ballX,float speed)
        {
            normal.Normalize();
            float width=Mathf.Clamp01(softness);
            // Keep an ordinary hand span close to the original pass, but let wide
            // hands absorb much more energy. The two extremes differ by almost 2x.
            float reboundSpeed=speed*Mathf.Lerp(1.14f,.58f,width*width);
            // A flat barrier gives a gentle pass. Players can steer by tilting either hand.
            // Underside contacts retain their downward direction instead of teleporting upward.
            if(normal.y<.05f)
                return Vector2.ClampMagnitude(velocity+normal*Mathf.Max(0,reboundSpeed*.85f-Vector2.Dot(velocity,normal)),reboundSpeed);
            float inward=ballX<0 ? 1f : -1f;
            float aim=Mathf.Clamp(inward*.43f+normal.x*.85f+velocity.x*.012f,-.85f,.85f);
            Vector2 direction=new Vector2(aim,Mathf.Max(.62f,normal.y)).normalized;
            return direction*reboundSpeed;
        }

        void EnsureStyles()
        {
            if(titleStyle != null) return;
            titleStyle=new GUIStyle(GUI.skin.label) { font=GuiFont,fontSize=25,fontStyle=FontStyle.Bold,normal={textColor=new Color(.95f,.97f,1)} };
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
            buttonStyle=new GUIStyle(GUI.skin.button) { font=GuiFont,fontSize=15,fontStyle=FontStyle.Bold };
            panelTexture=new Texture2D(1,1); panelTexture.SetPixel(0,0,new Color(.055f,.076f,.14f,.97f)); panelTexture.Apply();
        }
        static Color PlayerColor(int side) => side<0 ? new Color(.36f,.91f,.96f) : new Color(1,.57f,.51f);

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
            var card=new Rect(486,18,308,cardHeight);
            GUI.BeginGroup(card);
            GUI.DrawTexture(new Rect(0,0,card.width,cardHeight),panelTexture);
            float start=(card.width-(32+12+countWidth+8+goalWidth))*.5f;
            GUI.DrawTexture(new Rect(start,10+(rowHeight-32)*.5f,32,32),coinTexture);
            GUI.Label(new Rect(start+44,10,countWidth,rowHeight),count,collectionCountStyle);
            GUI.Label(new Rect(start+52+countWidth,10,goalWidth,rowHeight),goal,goalStyle);
            for(int i=0;i<CoinsPerRound;i++)
            {
                Color old=GUI.color;
                GUI.color=i<CollectedCount ? Color.white : new Color(.36f,.39f,.48f,.6f);
                GUI.DrawTexture(new Rect(50+i*24,rowHeight+16,16,16),coinTexture);
                GUI.color=old;
            }
            GUI.EndGroup();
        }

        void DrawVersusHud()
        {
            GUI.DrawTexture(new Rect(440,8,400,94),panelTexture);
            DrawPlayerScore(464,-1,versus.LeftScore,leftScoreStyle);
            DrawPlayerScore(716,1,versus.RightScore,rightScoreStyle);
            Fill(new Rect(637,38,6,6),new Color(.49f,.55f,.67f));
            Fill(new Rect(637,56,6,6),new Color(.49f,.55f,.67f));
        }

        void DrawPlayerScore(float x,int side,int score,GUIStyle style)
        {
            Color color=PlayerColor(side);
            float age=serveDuration-serveRemaining;
            bool scored=lastPointWinner==side && WaitingToServe;
            float pop=scored ? Mathf.Sin(Mathf.Clamp01(age/.65f)*Mathf.PI) : 0;
            style.fontSize=Mathf.RoundToInt(52+pop*10);
            GUI.Label(new Rect(x,12-pop*3,100,65),score.ToString(),style);
            for(int i=0;i<RallyVersusState.PointsToWin;i++)
            {
                Color pip=color; pip.a=i<score ? 1 : .18f;
                Fill(new Rect(x+24+i*20,84,12,4),pip);
            }
            if(scored && age<.85f)
            {
                color.a=Mathf.Clamp01(1-age/.85f);
                pointStyle.normal.textColor=color;
                GUI.Label(new Rect(x+78,26-age*16,45,32),"+1",pointStyle);
            }
            if(versus.SoftenedSide==side)
            {
                Fill(new Rect(x+6,110,88,32),new Color(.21f,.10f,.34f,.95f));
                DrawSoftReturnGlyph(new Vector2(x+24,126),10,new Color(.79f,.57f,1));
                GUI.Label(new Rect(x+38,112,52,28),"−25%",effectStyle);
            }
        }

        static void Stroke(Vector2 from,Vector2 to,float thickness,Color color)
        {
            Vector2 delta=to-from;
            Matrix4x4 old=GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,from);
            Fill(new Rect(from.x,from.y-thickness*.5f,delta.magnitude,thickness),color);
            GUI.matrix=old;
        }

        static void DrawSoftReturnGlyph(Vector2 center,float radius,Color color)
        {
            Vector2 top=center+Vector2.down*radius, right=center+Vector2.right*radius;
            Vector2 bottom=center+Vector2.up*radius, left=center+Vector2.left*radius;
            Stroke(top,right,1.5f,color); Stroke(right,bottom,1.5f,color);
            Stroke(bottom,left,1.5f,color); Stroke(left,top,1.5f,color);
            for(int i=0;i<2;i++)
            {
                Vector2 middle=center+Vector2.up*(i*4-2);
                Stroke(middle+new Vector2(-4,-2),middle+Vector2.up,1.5f,color);
                Stroke(middle+Vector2.up,middle+new Vector2(4,-2),1.5f,color);
            }
        }

        void DrawServeCue(float scale,Vector2 offset)
        {
            Vector3 screen=cameraView.WorldToScreenPoint(ball.transform.position);
            Vector2 point=(new Vector2(screen.x,Screen.height-screen.y)-offset)/scale;
            float progress=1-Mathf.Clamp01(serveRemaining/Mathf.Max(.01f,serveDuration));
            Fill(new Rect(point.x-33,point.y+27,66,22),new Color(.055f,.076f,.14f,.92f));
            for(int i=0;i<3;i++)
            {
                Color color=PlayerColor(servingSide);
                color.a=Mathf.Lerp(.2f,1,Mathf.Clamp01(progress*3-i));
                Fill(new Rect(point.x-21+i*16,point.y+35,10,5),color);
            }
        }

        void DrawVictory()
        {
            float x=versus.Winner<0 ? 320 : 960;
            Color color=PlayerColor(versus.Winner);
            GUI.DrawTexture(new Rect(x-108,240,216,132),panelTexture);
            Fill(new Rect(x-108,240,216,3),color);
            // The crown and card sit on the winning player's own half of the court.
            Vector2 a=new Vector2(x-22,279), b=new Vector2(x-17,301);
            Stroke(a,b,3,color); Stroke(b,new Vector2(x+17,301),3,color);
            Stroke(new Vector2(x+17,301),new Vector2(x+22,279),3,color);
            Stroke(a,new Vector2(x-10,286),3,color);
            Stroke(new Vector2(x-10,286),new Vector2(x,272),3,color);
            Stroke(new Vector2(x,272),new Vector2(x+10,286),3,color);
            Stroke(new Vector2(x+10,286),new Vector2(x+22,279),3,color);
            winStyle.normal.textColor=color;
            GUI.Label(new Rect(x-90,307,180,54),"WIN",winStyle);
        }

        void DrawRoundClear()
        {
            GUI.DrawTexture(new Rect(520,290,240,118),panelTexture);
            GUI.DrawTexture(new Rect(558,307,44,44),coinTexture);
            GUI.Label(new Rect(616,297,104,64),"+"+CoinsPerRound,scoreStyle);
            GUI.Label(new Rect(540,371,200,24),$"累計 {TeamScore}",matchHintStyle);
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
            if(IsVersus) DrawVersusHud();
            else DrawCollectionHud();
            if(GUI.Button(new Rect(1063,19,83,35),paused ? "再開" : "一時停止",buttonStyle)) SetPaused(!paused);
            if(GUI.Button(new Rect(1155,19,94,35),"設定",buttonStyle)) { settingsOpen=!settingsOpen; ApplySimulationState(); }
            if(GUI.Button(new Rect(1063,65,186,29),IsVersus && versus.Complete ? "もう一度" : "はじめから",buttonStyle)) RestartGame();

            if(settingsOpen) DrawSettings();
            else if(!inputReady) DrawMessage(InputWaitTitle,InputWaitDetail,new Color(.44f,.83f,.92f));
            else if(paused)
            {
                GUI.DrawTexture(new Rect(600,316,80,88),panelTexture);
                Fill(new Rect(621,340,12,40),new Color(.76f,.82f,.95f));
                Fill(new Rect(647,340,12,40),new Color(.76f,.82f,.95f));
            }
            else if(IsVersus && versus.Complete) DrawVictory();
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
            Fill(new Rect(0,110,1280,510),new Color(.015f,.025f,.06f,.72f));
            GUI.DrawTexture(new Rect(380,100,520,504),panelTexture);
            GUI.Label(new Rect(414,120,420,40),"遊びやすさを調整",titleStyle);
            GUI.enabled=Mode!=RallyMode.StarCollection;
            if(GUI.Button(new Rect(414,168,216,36),"星あつめ",buttonStyle)) SetMode(RallyMode.StarCollection);
            GUI.enabled=Mode!=RallyMode.Versus;
            if(GUI.Button(new Rect(646,168,216,36),"対戦 · 3点先取",buttonStyle)) SetMode(RallyMode.Versus);
            GUI.enabled=true;
            GUI.Label(new Rect(414,208,452,25),"モードを切り替えると、新しいゲームが始まります",smallStyle);
            GUI.Label(new Rect(414,239,420,30),$"ネットの高さ   {NetHeight:F1}",bodyStyle);
            NetHeight=GUI.HorizontalSlider(new Rect(418,282,438,22),NetHeight,1f,3.1f);
            GUI.Label(new Rect(414,307,440,25),"低いほど、ふたりでラリーをつなげやすい",smallStyle);
            GUI.Label(new Rect(414,355,420,30),$"ボールの速さ   {BallSpeed:F1}",bodyStyle);
            BallSpeed=GUI.HorizontalSlider(new Rect(418,398,438,22),BallSpeed,6.5f,10.5f);
            GUI.Label(new Rect(414,423,440,25),"手の幅を狭めると強く、傾けると左右へ",smallStyle);
            GUI.Label(new Rect(414,466,270,28),"ボールの光",bodyStyle);
            if(GUI.Button(new Rect(746,461,120,34),BallLightEnabled ? "ON" : "OFF",buttonStyle)) BallLightEnabled=!BallLightEnabled;
            GUI.Label(new Rect(414,500,452,25),IsVersus ? "紫のアイテム：相手の次の返球を25％弱くする" : "光が届く範囲だけ、星が見えて取れます",smallStyle);
            if(GUI.Button(new Rect(414,544,452,36),"ゲームに戻る",buttonStyle)) { settingsOpen=false; ApplySimulationState(); }
        }

        void OnEnable() { if(configured) ApplySimulationState(); }
        void OnDisable()
        {
            if(ball==null) return;
            ball.CancelBarrierImpact();
            ball.SetGameplaySimulation(false);
        }
        void OnDestroy()
        {
            if(ball != null) ball.CancelBarrierImpact();
            if(ball != null && ball.CooperativeGame==this) ball.CooperativeGame=null;
            CleanupVersusArtwork();
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
