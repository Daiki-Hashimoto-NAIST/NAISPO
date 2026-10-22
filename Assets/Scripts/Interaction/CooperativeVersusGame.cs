using UnityEngine;

namespace Naispo.Interaction
{
    public sealed partial class CooperativeCoinGame
    {
        const float ItemTelegraphSeconds = .6f;
        const float ItemAvailableSeconds = 7f;
        readonly RallyVersusState versus = new RallyVersusState();
        GameObject versusItemRoot;
        SpriteRenderer versusItemHalo;
        SpriteRenderer versusLeftFloor, versusRightFloor;
        readonly SpriteRenderer[] versusItemGlyph = new SpriteRenderer[8];
        Vector2 itemAnchor;
        float itemAge, itemSpawnDelay;
        bool itemVisible;

        public RallyMode Mode { get; private set; } = RallyMode.StarCollection;
        public bool IsVersus => Mode == RallyMode.Versus;
        public RallyVersusState VersusState => versus;
        public bool ItemVisible => IsVersus && versus.RallyActive && !versus.Complete && itemVisible;
        public bool ItemCollectible => ItemVisible && itemAge >= ItemTelegraphSeconds;
        public Vector2 ItemPosition => ItemPositionAtAge(itemAge);
        public float ItemAge => itemAge;
        public float ItemSecondsUntilSpawn => Mathf.Max(0,itemSpawnDelay);

        public void SetMode(RallyMode mode)
        {
            if(mode == Mode || (mode != RallyMode.StarCollection && mode != RallyMode.Versus)) return;
            Mode = mode;
            RefreshPresentation();
            settingsOpen = false;
            RestartGame();
            if(configured) RefreshLighting();
            RefreshVersusArtwork();
        }

        void BuildVersusArtwork()
        {
            versusLeftFloor = Rectangle("Left match floor",new Vector2(center.x-halfWidth*.5f,MatchFloor),
                new Vector2(halfWidth-.25f,.022f),new Color(.502f,.910f,.957f,.48f),2);
            versusRightFloor = Rectangle("Right match floor",new Vector2(center.x+halfWidth*.5f,MatchFloor),
                new Vector2(halfWidth-.25f,.022f),new Color(1,.698f,.549f,.48f),2);
            versusItemRoot = CreateChild("Soft return item",arenaRoot.transform);
            var halo = CreateChild("Purple halo",versusItemRoot.transform);
            versusItemHalo = halo.AddComponent<SpriteRenderer>();
            versusItemHalo.sprite = haloSprite;
            versusItemHalo.sortingOrder = 6;
            halo.transform.localScale = Vector3.one*1.05f;

            // A diamond and downward chevrons remain distinct from the collectible stars.
            Vector2[] corners = {new Vector2(0,.34f),new Vector2(.34f,0),new Vector2(0,-.34f),new Vector2(-.34f,0)};
            for(int i=0;i<4;i++)
                versusItemGlyph[i] = ItemStroke("Diamond "+i,corners[i],corners[(i+1)%4],.035f);
            versusItemGlyph[4] = ItemStroke("Upper arrow left",new Vector2(-.13f,.12f),new Vector2(0,.015f),.045f);
            versusItemGlyph[5] = ItemStroke("Upper arrow right",new Vector2(0,.015f),new Vector2(.13f,.12f),.045f);
            versusItemGlyph[6] = ItemStroke("Lower arrow left",new Vector2(-.13f,-.015f),new Vector2(0,-.12f),.045f);
            versusItemGlyph[7] = ItemStroke("Lower arrow right",new Vector2(0,-.12f),new Vector2(.13f,-.015f),.045f);
            versusItemRoot.SetActive(false);
            RefreshVersusArtwork();
        }

        SpriteRenderer ItemStroke(string name,Vector2 from,Vector2 to,float thickness)
        {
            var go = CreateChild(name,versusItemRoot.transform);
            Vector2 delta = to-from;
            go.transform.localPosition = (from+to)*.5f;
            go.transform.localRotation = Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
            go.transform.localScale = new Vector3(delta.magnitude,thickness,1);
            var stroke = go.AddComponent<SpriteRenderer>();
            stroke.sprite = whiteSprite;
            stroke.sortingOrder = 7;
            return stroke;
        }

        void ResetVersusRally()
        {
            if(IsVersus) versus.BeginRally();
            itemVisible = false;
            itemAge = 0;
            itemSpawnDelay = Random.Range(1.2f,2.2f);
            RefreshVersusArtwork();
        }

        void UpdateVersus(float dt,Vector2 previousBall,Vector2 currentBall,float pickupRadius)
        {
            if(!IsVersus || !versus.RallyActive || versus.Complete || dt<=0) return;
            if(!itemVisible)
            {
                // A serve alone cannot claim an item. Do not stack effects while one is pending.
                if(versus.LastHitter == 0 || versus.SoftenedSide != 0) return;
                itemSpawnDelay -= dt;
                if(itemSpawnDelay<=0) SpawnVersusItem();
                return;
            }

            float oldAge = itemAge;
            itemAge += dt;
            float lifetime = ItemTelegraphSeconds+ItemAvailableSeconds;
            float availableFrom = Mathf.Max(oldAge,ItemTelegraphSeconds);
            float availableTo = Mathf.Min(itemAge,lifetime);
            if(availableFrom<=availableTo && itemAge>=ItemTelegraphSeconds)
            {
                // Clip the ball's sweep to the collectible interval, including slow frames
                // that cross the end of the warning or the item's disappearance time.
                float fromT = Mathf.Clamp01((availableFrom-oldAge)/dt);
                float toT = Mathf.Clamp01((availableTo-oldAge)/dt);
                Vector2 from = Vector2.Lerp(previousBall,currentBall,fromT)-ItemPositionAtAge(availableFrom);
                Vector2 to = Vector2.Lerp(previousBall,currentBall,toT)-ItemPositionAtAge(availableTo);
                if(SweptHit(from,to,Vector2.zero,pickupRadius) && versus.CollectItem())
                {
                    DismissVersusItem();
                    return;
                }
            }
            if(itemAge>=lifetime) DismissVersusItem();
        }

        void SpawnVersusItem()
        {
            float lowest = Mathf.Max(ArenaFloor+netHeight+.75f,center.y+.25f);
            float highest = Mathf.Max(lowest,Mathf.Min(center.y+halfHeight*.42f,center.y+halfHeight-1.5f));
            itemAnchor = new Vector2(center.x+Random.Range(-halfWidth*.36f,halfWidth*.36f),Random.Range(lowest,highest));
            itemVisible = true;
            itemAge = 0;
            itemSpawnDelay = 0;
        }

        void DismissVersusItem()
        {
            itemVisible = false;
            itemAge = 0;
            itemSpawnDelay = Random.Range(4f,7f);
        }

        Vector2 ItemPositionAtAge(float age) => itemAnchor+Vector2.up*(Mathf.Sin(age*2.1f)*.07f);

        void RefreshVersusArtwork()
        {
            if(versusLeftFloor != null) versusLeftFloor.enabled = IsVersus;
            if(versusRightFloor != null) versusRightFloor.enabled = IsVersus;
            if(versusItemRoot == null) return;
            bool visible = ItemVisible;
            versusItemRoot.SetActive(visible);
            if(!visible) return;
            // This method only renders state: pause, camera loss and settings freeze the item.
            float arrival = Mathf.SmoothStep(0,1,Mathf.Clamp01(itemAge/ItemTelegraphSeconds));
            float pulse = .97f+.03f*Mathf.Sin(itemAge*4);
            float fade = Mathf.Clamp01((ItemTelegraphSeconds+ItemAvailableSeconds-itemAge)/.45f);
            float alpha = Mathf.Lerp(.28f,1,arrival)*fade;
            versusItemRoot.transform.position = ItemPosition;
            versusItemRoot.transform.localScale = Vector3.one*Mathf.Lerp(.65f,1,arrival)*pulse*.84f;
            versusItemHalo.color = new Color(.69f,.47f,1,.22f*alpha);
            for(int i=0;i<versusItemGlyph.Length;i++)
                versusItemGlyph[i].color = i<4 ? new Color(.72f,.46f,1,alpha) : new Color(.95f,.83f,1,alpha);
        }

        void CleanupVersusArtwork()
        {
            Release(versusItemRoot);
            Release(versusLeftFloor != null ? versusLeftFloor.gameObject : null);
            Release(versusRightFloor != null ? versusRightFloor.gameObject : null);
            versusItemRoot = null;
            versusLeftFloor = versusRightFloor = null;
        }
    }
}
