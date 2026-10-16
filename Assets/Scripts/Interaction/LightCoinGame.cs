using UnityEngine;
using Naispo.Core;

namespace Naispo.Interaction
{
    // Small, repeatable rounds; the same ball is reused so its spotlight keeps following.
    public sealed class LightCoinGame : MonoBehaviour
    {
        const int CoinCount = 6;
        readonly SpriteRenderer[] coins = new SpriteRenderer[CoinCount];
        readonly bool[] collected = new bool[CoinCount];
        KinectSilhouetteManager manager;
        AirHockeyBall ball;
        Camera cameraView;
        Sprite coinSprite;
        Texture2D coinTexture;
        Vector2 previous, center;
        float halfWidth, halfHeight, respawnAt = -1, roundAt = -1;
        int score, round = 1, misses;
        SpriteRenderer ballView;

        void Start()
        {
            manager = GetComponent<KinectSilhouetteManager>();
            cameraView = manager.mapper.targetCamera;
            ball = FindFirstObjectByType<AirHockeyBall>();
            if(ball == null || cameraView == null) { enabled=false; return; }
            ballView = ball.GetComponent<SpriteRenderer>();
            ball.GravityGame = true;
            ball.Body.gravityScale = 0.65f;
            ball.maximumSpeed = 13;
            SetupArena();
            coinTexture = new Texture2D(32,32,TextureFormat.RGBA32,false);
            for(int y=0;y<32;y++) for(int x=0;x<32;x++)
            {
                float d = Vector2.Distance(new Vector2(x,y),new Vector2(15.5f,15.5f));
                coinTexture.SetPixel(x,y,d < 14 ? (d > 11 ? new Color(1,0.6f,0.06f) : new Color(1,0.9f,0.16f)) : Color.clear);
            }
            coinTexture.Apply();
            coinSprite = Sprite.Create(coinTexture,new Rect(0,0,32,32),new Vector2(0.5f,0.5f),80);
            for(int i=0;i<CoinCount;i++)
            {
                var go = new GameObject("Hidden coin " + (i+1));
                go.transform.SetParent(transform,false);
                coins[i] = go.AddComponent<SpriteRenderer>();
                coins[i].sprite = coinSprite;
                coins[i].sortingOrder = 5;
                coins[i].enabled = false;
            }
            PlaceCoins();
            Respawn();
        }

        void SetupArena()
        {
            center = cameraView.transform.position;
            halfHeight = cameraView.orthographicSize;
            halfWidth = Mathf.Min(halfHeight*0.65f,halfHeight*cameraView.aspect-0.4f);
            var walls = FindFirstObjectByType<ScreenBoundaryController>();
            if(walls == null) return;
            walls.enabled = false;
            walls.bottomWall.gameObject.SetActive(false);
            SetWall(walls.leftWall,center+new Vector2(-halfWidth-0.15f,0),new Vector2(0.3f,halfHeight*2));
            SetWall(walls.rightWall,center+new Vector2(halfWidth+0.15f,0),new Vector2(0.3f,halfHeight*2));
            SetWall(walls.topWall,center+new Vector2(0,halfHeight+0.15f),new Vector2(halfWidth*2+0.6f,0.3f));
        }
        static void SetWall(BoxCollider2D wall,Vector2 position,Vector2 size)
        {
            wall.transform.position = position;
            wall.size = size;
            var view = wall.GetComponent<SpriteRenderer>();
            if(view != null) { view.size=size; view.color=new Color(0.18f,0.25f,0.32f); }
        }
        void PlaceCoins()
        {
            for(int i=0;i<CoinCount;i++)
            {
                collected[i] = false;
                float x = ((i%3)-1)*halfWidth*0.65f;
                float y = halfHeight*(i<3 ? 0.12f : 0.55f);
                if(round%2==0) x += (i<3 ? 0.2f : -0.2f);
                coins[i].transform.position = center + new Vector2(x,y);
                coins[i].enabled = false;
            }
        }
        void Respawn()
        {
            ball.Body.simulated=true;
            Vector2 spawn = center + new Vector2(0,halfHeight*0.72f);
            ball.transform.position = new Vector3(spawn.x,spawn.y,ball.transform.position.z);
            ball.Body.position = spawn;
            ball.Body.linearVelocity = new Vector2(misses%2==0 ? 0.7f : -0.7f,0);
            ball.Body.angularVelocity=0;
            ballView.enabled=true;
            previous=ball.Body.position;
            respawnAt=-1;
            Debug.Log("[Coin game] Respawn at " + spawn);
        }
        void Update()
        {
            if(ball == null) return;
            if(respawnAt >= 0)
            {
                if(Time.time >= respawnAt) Respawn();
                else return;
            }
            Vector2 position=ball.Body.position;
            if(position.y < center.y-halfHeight-0.5f)
            {
                misses++;
                Debug.Log("[Coin game] Drop " + misses + " at " + position);
                ball.Body.simulated=false;
                ballView.enabled=false;
                respawnAt=Time.time+0.8f;
                foreach(var coin in coins) coin.enabled=false;
                return;
            }
            if(roundAt >= 0 && Time.time >= roundAt) { round++; PlaceCoins(); roundAt=-1; }
            bool light=manager.silhouetteRenderer.LightEnabled && manager.IsFresh;
            float radius=manager.settings.ballLightRadius;
            float ballRadius=ball.GetComponent<CircleCollider2D>().bounds.extents.x;
            int remaining=0;
            for(int i=0;i<CoinCount;i++)
            {
                if(collected[i]) continue;
                Vector2 p=coins[i].transform.position;
                float brightness=light ? RevealAmount(Vector2.Distance(p,position),radius) : 0;
                coins[i].enabled=brightness>0.001f;
                coins[i].color=new Color(1,1,1,brightness);
                if(light && SweptHit(previous,position,p,ballRadius+0.18f))
                { collected[i]=true; coins[i].enabled=false; score++; }
                else remaining++;
            }
            if(remaining==0 && roundAt<0) roundAt=Time.time+1.2f;
            previous=position;
        }
        public static float RevealAmount(float distance,float radius)
        {
            return 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(radius*0.55f,radius,distance));
        }
        public static bool SweptHit(Vector2 from,Vector2 to,Vector2 coin,float radius)
        {
            Vector2 delta=to-from;
            float t=delta.sqrMagnitude>0 ? Mathf.Clamp01(Vector2.Dot(coin-from,delta)/delta.sqrMagnitude) : 0;
            return (coin-(from+delta*t)).sqrMagnitude<=radius*radius;
        }
        public static Vector2 BoostReflection(Vector2 velocity,Vector2 normal,float softness)
        {
            normal.Normalize();
            float speed=Mathf.Lerp(10.5f,7.5f,Mathf.Clamp01(softness));
            return Vector2.ClampMagnitude(velocity+normal*Mathf.Max(0,speed-Vector2.Dot(velocity,normal)),13);
        }
        void OnGUI()
        {
            if(ball == null) return;
            GUI.Box(new Rect(Screen.width-240,12,228,110),GUIContent.none);
            GUI.Label(new Rect(Screen.width-225,22,210,28),$"COINS  {score}     ROUND  {round}");
            GUI.Label(new Rect(Screen.width-225,49,210,25),respawnAt>=0 ? "Respawning..." : roundAt>=0 ? "All coins collected!" : $"Drops: {misses}");
            if(GUI.Button(new Rect(Screen.width-225,80,195,28),"Restart game"))
            { score=0; round=1; misses=0; roundAt=-1; PlaceCoins(); Respawn(); }
        }
        void OnDestroy()
        {
            foreach(var coin in coins) if(coin != null) Destroy(coin.gameObject);
            if(coinSprite != null) Destroy(coinSprite);
            if(coinTexture != null) Destroy(coinTexture);
        }
    }
}
