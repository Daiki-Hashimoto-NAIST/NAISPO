using UnityEngine;

namespace Naispo.Interaction
{
    public sealed partial class CooperativeCoinGame
    {
        RallyBattleEffects battleEffects;
        float battlePointAge=10, battleWinAge;
        GUIStyle battlePointStyle;
        public RallyBattleEffects BattleEffects => battleEffects;

        void BuildBattlePresentation()
        {
            battleEffects=CreateChild("Battle feedback",arenaRoot.transform).AddComponent<RallyBattleEffects>();
            battleEffects.Initialize(cameraView,halfWidth,MatchFloor,center);
            battleEffects.SetVisible(IsVersus);
        }
        bool BattleSuspended => !isActiveAndEnabled || WaitingForPlayerInput || paused || settingsOpen || ExternalOverlayOpen;
        void SyncBattleSuspension()
        {
            if(battleEffects!=null) battleEffects.SetSuspended(BattleSuspended);
        }
        void AdvanceBattlePresentation(float dt)
        {
            SyncBattleSuspension();
            if(!IsVersus || BattleSuspended) return;
            battlePointAge+=dt;
            if(versus.Complete) battleWinAge+=dt;
            if(battleEffects!=null) battleEffects.Step(dt);
        }
        void ResetBattlePresentation()
        {
            battlePointAge=10; battleWinAge=0;
            if(battleEffects!=null) battleEffects.ResetEffects();
        }
        public void NotifyBarrierReleased(Vector2 position,int playerSide,float handBoost)
        {
            if(!IsVersus || !CanAdvanceBall || battleEffects==null) return;
            battleEffects.Hit(position,playerSide,Mathf.Clamp01(handBoost));
        }
        void CelebratePoint(Vector2 floorContact)
        {
            battlePointAge=0; battleWinAge=0;
            if(battleEffects!=null) battleEffects.Goal(floorContact,lastPointWinner,versus.Complete);
        }
        void DrawBattlePoint()
        {
            if(lastPointWinner==0 || battlePointAge>=1.15f || versus.Complete) return;
            if(battlePointStyle==null) battlePointStyle=new GUIStyle(centeredStyle) {
                fontSize=82,fontStyle=FontStyle.Bold,wordWrap=false,padding=new RectOffset(0,0,0,0)
            };
            float age=battlePointAge;
            float pop=1+Mathf.Sin(Mathf.Clamp01(age/.35f)*Mathf.PI)*.28f;
            float alpha=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.65f,1.15f,age));
            float x=lastPointWinner<0 ? 320 : 960;
            float y=245-Mathf.Clamp01(age/1.15f)*22;
            Color color=PlayerColor(lastPointWinner);color.a=alpha;
            Matrix4x4 old=GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one*pop,new Vector2(x,y+48));
            battlePointStyle.normal.textColor=color;
            GUI.Label(new Rect(x-110,y,220,96),"+1",battlePointStyle);
            Color soft=color;soft.a*=.55f;
            float reach=74+Mathf.Clamp01(age/.35f)*45;
            Stroke(new Vector2(x-reach-20,y+48),new Vector2(x-85,y+48),3,soft);
            Stroke(new Vector2(x+85,y+48),new Vector2(x+reach+20,y+48),3,soft);
            GUI.matrix=old;
        }
        void DrawBattleVictory()
        {
            float x=versus.Winner<0 ? 320 : 960;
            Color color=PlayerColor(versus.Winner);
            float age=battleWinAge;
            float arrival=1-Mathf.Exp(-age*10);
            float pop=.72f+.28f*arrival+Mathf.Sin(age*14)*.10f*Mathf.Exp(-age*3);
            Vector2 pivot=new Vector2(x,292);
            Matrix4x4 old=GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one*pop,pivot);

            Color dim=color;dim.a=.22f;
            float reach=170*arrival;
            Stroke(new Vector2(x-reach,211),new Vector2(x+reach,211),2,dim);
            Stroke(new Vector2(x-reach,389),new Vector2(x+reach,389),2,dim);
            // Rune circles and gold sparks replace the old large translucent panel.
            Color gold=visualConfig!=null?visualConfig.ballGold:new Color(.95f,.79f,.46f);
            gold.a=.28f+.34f*arrival;
            for(int side=-1;side<=1;side+=2)
            {
                float cx=x+side*142;
                Vector2 previous=new Vector2(cx+31,300);
                for(int i=1;i<=16;i++)
                {
                    float angle=i*Mathf.PI*2/16;
                    Vector2 point=new Vector2(cx+Mathf.Cos(angle)*31,300+Mathf.Sin(angle)*31);
                    Stroke(previous,point,1.3f,gold);previous=point;
                }
            }
            for(int i=0;i<12;i++)
            {
                float angle=i*Mathf.PI*2/12+.3f;
                float inner=92+arrival*34,outer=inner+11;
                Color spark=gold;spark.a*=.7f;
                Stroke(new Vector2(x+Mathf.Cos(angle)*inner,292+Mathf.Sin(angle)*inner),
                    new Vector2(x+Mathf.Cos(angle)*outer,292+Mathf.Sin(angle)*outer),2,spark);
            }
            for(int sign=-1;sign<=1;sign+=2)
            {
                float edge=x+sign*184;
                Stroke(new Vector2(edge-sign*22,211),new Vector2(edge,211),3,color);
                Stroke(new Vector2(edge,211),new Vector2(edge,233),3,color);
                Stroke(new Vector2(edge-sign*22,389),new Vector2(edge,389),3,color);
                Stroke(new Vector2(edge,389),new Vector2(edge,367),3,color);
            }
            Vector2 a=new Vector2(x-28,244),b=new Vector2(x-22,270);
            Stroke(a,b,3,color);Stroke(b,new Vector2(x+22,270),3,color);
            Stroke(new Vector2(x+22,270),new Vector2(x+28,244),3,color);
            Stroke(a,new Vector2(x-12,252),3,color);
            Stroke(new Vector2(x-12,252),new Vector2(x,236),3,color);
            Stroke(new Vector2(x,236),new Vector2(x+12,252),3,color);
            Stroke(new Vector2(x+12,252),new Vector2(x+28,244),3,color);
            winStyle.fontSize=82;winStyle.fontStyle=FontStyle.Bold;winStyle.normal.textColor=color;
            GUI.Label(new Rect(x-155,273,310,102),"WIN",winStyle);
            GUI.matrix=old;
        }
    }
}
