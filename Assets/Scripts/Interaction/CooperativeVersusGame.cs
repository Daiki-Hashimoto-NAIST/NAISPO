using UnityEngine;

namespace Naispo.Interaction
{
    public sealed partial class CooperativeCoinGame
    {
        public const int PickupSlotCount = 2;
        public const float PickupFirstDelay = .9f;
        public const float PickupRetryDelay = 2.4f;
        public const float PickupTelegraphSeconds = .5f;
        public const float PickupAvailableSeconds = 6f;
        public const float PickupSeparation = 1.3f;
        const int PickupSeed = 0x5EED1A7;

        sealed class PickupSlot
        {
            public bool Active;
            public RallySpell Spell;
            public Vector2 Position;
            public float Age;
        }

        sealed class PickupVisual
        {
            public GameObject Root;
            public SpriteRenderer Halo;
            public readonly GameObject[] TypeRoot = new GameObject[4];
            public readonly SpriteRenderer[][] Strokes = new SpriteRenderer[4][];
        }

        struct PickupSweep
        {
            public int Slot;
            public float Along;
        }

        readonly RallyVersusState versus = new RallyVersusState();
        readonly PickupSlot[] pickupSlots = new PickupSlot[PickupSlotCount];
        readonly PickupVisual[] pickupVisuals = new PickupVisual[PickupSlotCount];
        readonly PickupSweep[] pickupSweeps = new PickupSweep[PickupSlotCount];
        RallyPickupBag pickupBag = new RallyPickupBag(PickupSeed);
        System.Random pickupPlacement = new System.Random(PickupSeed);
        GameObject versusPickupRoot;
        GameObject ballSpellRoot;
        SpriteRenderer ballSpellHalo;
        readonly GameObject[] ballSpellTypeRoot = new GameObject[4];
        readonly SpriteRenderer[][] ballSpellStrokes = new SpriteRenderer[4][];
        readonly SpriteRenderer[] lightWarningRing = new SpriteRenderer[20];
        readonly Sprite[] spellArtwork = new Sprite[4];
        readonly Texture2D[] generatedSpellTextures = new Texture2D[4];
        SpriteRenderer versusLeftFloor, versusRightFloor;
        bool pickupArmed;
        int nextPickupSide = -1;
        float pickupSpawnDelay = -1;

        public RallyMode Mode { get; private set; } = RallyMode.StarCollection;
        public bool IsVersus => Mode == RallyMode.Versus;
        public RallyVersusState VersusState => versus;
        public RallySpell ActiveSpell => versus.ActiveSpell;
        public int SpellOwner => versus.SpellOwner;
        public float SpellSecondsRemaining => versus.SpellSecondsRemaining;
        public int PickupCount
        {
            get
            {
                if (!IsVersus || !versus.RallyActive || versus.Complete) return 0;
                int count = 0;
                for (int i = 0; i < pickupSlots.Length; i++) if (pickupSlots[i].Active) count++;
                return count;
            }
        }
        public float PickupSecondsUntilSpawn => pickupArmed ? Mathf.Max(0, pickupSpawnDelay) : 0;

        public RallyPickupSnapshot GetPickupSnapshot(int slot)
        {
            if (slot < 0 || slot >= pickupSlots.Length) return default;
            var entry = pickupSlots[slot];
            bool visible = IsVersus && versus.RallyActive && !versus.Complete && entry.Active;
            bool collectible = visible && entry.Age >= PickupTelegraphSeconds
                && entry.Age < PickupTelegraphSeconds + PickupAvailableSeconds;
            return new RallyPickupSnapshot(entry.Spell, entry.Position, entry.Age, visible, collectible);
        }

        public void SetMode(RallyMode mode)
        {
            if (mode == Mode || (mode != RallyMode.StarCollection && mode != RallyMode.Versus)) return;
            Mode = mode;
            RefreshPresentation();
            settingsOpen = false;
            RestartGame();
            if (configured) RefreshLighting();
            RefreshVersusArtwork();
        }

        public bool TryActivateSpell(RallySpell spell)
        {
            if (!IsVersus || !CanAdvanceBall || spell == RallySpell.None) return false;
            if (!versus.TryActivateSpell(spell)) return false;
            if (spell == RallySpell.Light) BeginLightWarning();
            else EndLightPhase();
            return true;
        }

        public Vector2 ApplyPendingSpellToRebound(Vector2 rebound)
        {
            if (!IsVersus || rebound.sqrMagnitude <= 0) return rebound;
            return rebound * versus.ConsumeSpellMultiplier();
        }

        internal void RegisterBarrierHit(int playerSide)
        {
            if (!IsVersus || !CanAdvanceBall) return;
            versus.RegisterBarrierHit(playerSide);
            if (!pickupArmed)
            {
                pickupArmed = true;
                pickupSpawnDelay = PickupFirstDelay;
            }
            CancelLightForContact();
        }

        void BuildVersusArtwork()
        {
            versusLeftFloor = Rectangle("Left match floor", new Vector2(center.x - halfWidth * .5f, MatchFloor),
                new Vector2(halfWidth - .25f, .035f), new Color(PlayerColor(-1).r, PlayerColor(-1).g, PlayerColor(-1).b, .75f), 2);
            versusRightFloor = Rectangle("Right match floor", new Vector2(center.x + halfWidth * .5f, MatchFloor),
                new Vector2(halfWidth - .25f, .035f), new Color(PlayerColor(1).r, PlayerColor(1).g, PlayerColor(1).b, .75f), 2);
            BuildBallSpellArtwork();
            versusPickupRoot = CreateChild("Spell pickups", arenaRoot.transform);
            for (int i = 0; i < pickupSlots.Length; i++)
            {
                pickupSlots[i] = new PickupSlot();
                pickupVisuals[i] = BuildPickupVisual(i);
            }
            RefreshVersusArtwork();
        }

        PickupVisual BuildPickupVisual(int index)
        {
            var visual = new PickupVisual();
            visual.Root = CreateChild("Spell pickup " + (index + 1), versusPickupRoot.transform);
            var halo = CreateChild("Halo", visual.Root.transform);
            visual.Halo = halo.AddComponent<SpriteRenderer>();
            visual.Halo.sprite = haloSprite;
            visual.Halo.sortingOrder = 6;
            halo.transform.localScale = Vector3.one * 1.05f;
            BuildSpellTypes(visual.Root.transform, visual.TypeRoot, visual.Strokes);
            visual.Root.SetActive(false);
            return visual;
        }

        void BuildBallSpellArtwork()
        {
            ballSpellRoot = CreateChild("Ball spell", arenaRoot.transform);
            var halo = CreateChild("Spell halo", ballSpellRoot.transform);
            ballSpellHalo = halo.AddComponent<SpriteRenderer>();
            ballSpellHalo.sprite = haloSprite;
            ballSpellHalo.sortingOrder = 9;
            halo.transform.localScale = Vector3.one * .72f;
            BuildSpellTypes(ballSpellRoot.transform, ballSpellTypeRoot, ballSpellStrokes);
            for (int i = 0; i < lightWarningRing.Length; i++)
            {
                float angle = i * Mathf.PI * 2 / lightWarningRing.Length;
                Vector2 start = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .30f;
                Vector2 end = new Vector2(Mathf.Cos(angle + Mathf.PI * 2 / lightWarningRing.Length),
                    Mathf.Sin(angle + Mathf.PI * 2 / lightWarningRing.Length)) * .30f;
                lightWarningRing[i] = Stroke("Light warning " + i, ballSpellRoot.transform, start, end, .022f, 11);
            }
            ballSpellRoot.SetActive(false);
        }

        void BuildSpellTypes(Transform parent, GameObject[] roots, SpriteRenderer[][] strokes)
        {
            roots[(int)RallySpell.Fireball] = BuildSpellGlyph("Fireball", parent, RallySpell.Fireball, out strokes[(int)RallySpell.Fireball]);
            roots[(int)RallySpell.Timer] = BuildSpellGlyph("Timer", parent, RallySpell.Timer, out strokes[(int)RallySpell.Timer]);
            roots[(int)RallySpell.Light] = BuildSpellGlyph("Light", parent, RallySpell.Light, out strokes[(int)RallySpell.Light]);
        }

        GameObject BuildSpellGlyph(string name, Transform parent, RallySpell spell, out SpriteRenderer[] strokes)
        {
            var root = CreateChild(name, parent);
            Sprite icon = GetSpellArtwork(spell);
            string rendererName = generatedSpellTextures[(int)spell] == null ? "Configured icon" : "Spell icon";
            var view = RallySceneryArtwork.AddRenderer(root.transform, rendererName, icon,
                Color.white, 10, simple: true);
            view.transform.localPosition = Vector3.zero;
            strokes = new[] { view };
            return root;
        }

        Sprite GetSpellArtwork(RallySpell spell)
        {
            int index = (int)spell;
            if (index <= 0 || index >= spellArtwork.Length) return null;
            if (spellArtwork[index] != null) return spellArtwork[index];
            var configuredIcon = CreateConfiguredSprite(visualConfig != null ? visualConfig.SpellIcon(spell) : null,
                .52f, "Configured " + spell + " icon", true);
            if (configuredIcon != null) return spellArtwork[index] = configuredIcon;
            var texture = RallySpellArtwork.Create(spell);
            generatedSpellTextures[index] = texture;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                Vector2.one * .5f, texture.width / RallySpellArtwork.WorldSize, 0, SpriteMeshType.FullRect);
            sprite.name = "Rally " + spell + " symbol";
            ownedVisualWrappers.Add(sprite);
            return spellArtwork[index] = sprite;
        }

        static Color SpellColor(RallySpell spell) => RallySpellArtwork.ColorFor(spell);

        SpriteRenderer Stroke(string name, Transform parent, Vector2 from, Vector2 to, float thickness, int order)
            => Stroke(name, parent, from, to, thickness, order, Color.white);

        SpriteRenderer Stroke(string name, Transform parent, Vector2 from, Vector2 to, float thickness, int order, Color color)
        {
            var go = CreateChild(name, parent);
            Vector2 delta = to - from;
            go.transform.localPosition = (from + to) * .5f;
            go.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            go.transform.localScale = new Vector3(delta.magnitude, thickness, 1);
            var stroke = go.AddComponent<SpriteRenderer>();
            stroke.sprite = whiteSprite;
            stroke.color = color;
            stroke.sortingOrder = order;
            return stroke;
        }

        void ResetVersusRally()
        {
            if (IsVersus) versus.BeginRally();
            ClearPickupSlots();
            // A point ends the pickup cycle. A spawn delay left over from the previous
            // rally must not fire during the next serve before a real return. The bag
            // and placement RNG deliberately survive between points; a match restart
            // still reseeds them in ResetPickupsForMatch.
            pickupArmed = false;
            pickupSpawnDelay = -1;
            ClearLightPhase();
            RefreshVersusArtwork();
        }

        void ResetPickupsForMatch()
        {
            pickupBag = new RallyPickupBag(PickupSeed);
            pickupPlacement = new System.Random(PickupSeed);
            pickupArmed = false;
            pickupSpawnDelay = -1;
            nextPickupSide = -1;
            ClearPickupSlots();
        }

        void ClearPickupSlots()
        {
            for (int i = 0; i < pickupSlots.Length; i++)
            {
                pickupSlots[i].Active = false;
                pickupSlots[i].Age = 0;
                pickupSlots[i].Spell = RallySpell.None;
            }
        }

        void UpdateVersus(float dt, Vector2 previousBall, Vector2 currentBall, float pickupRadius)
        {
            if (!IsVersus || !versus.RallyActive || versus.Complete || dt <= 0) return;
            int sweepCount = 0;
            float lifetime = PickupTelegraphSeconds + PickupAvailableSeconds;
            for (int i = 0; i < pickupSlots.Length; i++)
            {
                var entry = pickupSlots[i];
                if (!entry.Active) continue;
                float oldAge = entry.Age;
                entry.Age += dt;
                float availableFrom = Mathf.Max(oldAge, PickupTelegraphSeconds);
                float availableTo = Mathf.Min(entry.Age, lifetime);
                if (availableFrom <= availableTo && entry.Age >= PickupTelegraphSeconds)
                {
                    float fromT = Mathf.Clamp01((availableFrom - oldAge) / dt);
                    float toT = Mathf.Clamp01((availableTo - oldAge) / dt);
                    Vector2 from = Vector2.Lerp(previousBall, currentBall, fromT);
                    Vector2 to = Vector2.Lerp(previousBall, currentBall, toT);
                    if (SweptHit(from, to, entry.Position, pickupRadius))
                    {
                        Vector2 delta = to - from;
                        float along = delta.sqrMagnitude > 0
                            ? Mathf.Clamp01(Vector2.Dot(entry.Position - from, delta) / delta.sqrMagnitude)
                            : 0;
                        // "Along" must be expressed on the full frame. Each slot only
                        // sweeps its own clipped window, so a slot whose window ends
                        // early would otherwise outrank a younger slot hit later in
                        // the same frame and reverse the final spell.
                        along = fromT + (toT - fromT) * along;
                        pickupSweeps[sweepCount++] = new PickupSweep { Slot = i, Along = along };
                    }
                }
            }

            // Multiple intersections are consumed in travel order; the slot index breaks ties.
            for (int i = 0; i < sweepCount; i++)
                for (int j = i + 1; j < sweepCount; j++)
                {
                    var a = pickupSweeps[i];
                    var b = pickupSweeps[j];
                    if (b.Along < a.Along || (Mathf.Approximately(b.Along, a.Along) && b.Slot < a.Slot))
                    {
                        pickupSweeps[i] = b;
                        pickupSweeps[j] = a;
                    }
                }

            for (int i = 0; i < sweepCount; i++)
            {
                var entry = pickupSlots[pickupSweeps[i].Slot];
                if (!entry.Active) continue;
                if (TryActivateSpell(entry.Spell))
                {
                    if (battleEffects != null) battleEffects.Pickup(entry.Position, versus.SpellOwner);
                    entry.Active = false;
                    entry.Age = 0;
                }
            }

            for (int i = 0; i < pickupSlots.Length; i++)
            {
                var entry = pickupSlots[i];
                if (entry.Active && entry.Age >= lifetime)
                {
                    entry.Active = false;
                    entry.Age = 0;
                }
            }

            if (!pickupArmed) return;
            pickupSpawnDelay -= dt;
            if (pickupSpawnDelay > 0) return;
            pickupSpawnDelay = PickupRetryDelay;
            if (PickupCount >= PickupSlotCount) return;
            TrySpawnPickup();
        }

        void TrySpawnPickup()
        {
            float netTop = ArenaFloor + netHeight;
            float lower = Mathf.Max(netTop + .7f, MatchFloor + 1.5f);
            float upper = Mathf.Min(center.y + .4f * halfHeight, center.y + halfHeight - .85f - .75f);
            if (upper < lower) return;
            int slot = -1;
            for (int i = 0; i < pickupSlots.Length; i++)
                if (!pickupSlots[i].Active) { slot = i; break; }
            if (slot < 0) return;

            int side = nextPickupSide;
            nextPickupSide = -nextPickupSide;
            Vector2 position = default;
            bool found = false;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float x = center.x + side * Mathf.Lerp(.22f, .60f, NextPlacement()) * halfWidth;
                float y = Mathf.Lerp(lower, upper, NextPlacement());
                var candidate = new Vector2(x, y);
                bool clear = true;
                for (int i = 0; i < pickupSlots.Length; i++)
                    if (pickupSlots[i].Active && Vector2.Distance(pickupSlots[i].Position, candidate) < PickupSeparation)
                    { clear = false; break; }
                if (!clear) continue;
                position = candidate;
                found = true;
                break;
            }
            if (!found) return;

            pickupSlots[slot].Active = true;
            pickupSlots[slot].Spell = pickupBag.Next();
            pickupSlots[slot].Position = position;
            pickupSlots[slot].Age = 0;
        }

        float NextPlacement() => (float)pickupPlacement.NextDouble();

        void RefreshVersusArtwork()
        {
            if (versusLeftFloor != null) versusLeftFloor.enabled = IsVersus;
            if (versusRightFloor != null) versusRightFloor.enabled = IsVersus;
            if (versusPickupRoot != null)
            {
                float lifetime = PickupTelegraphSeconds + PickupAvailableSeconds;
                for (int i = 0; i < pickupSlots.Length; i++)
                {
                    var entry = pickupSlots[i];
                    var visual = pickupVisuals[i];
                    if (visual == null) continue;
                    bool visible = IsVersus && versus.RallyActive && !versus.Complete && entry.Active;
                    visual.Root.SetActive(visible);
                    if (!visible) continue;
                    float arrival = Mathf.SmoothStep(0, 1, Mathf.Clamp01(entry.Age / PickupTelegraphSeconds));
                    float pulse = .97f + .03f * Mathf.Sin(entry.Age * 4);
                    float fade = Mathf.Clamp01((lifetime - entry.Age) / .35f);
                    float alpha = Mathf.Lerp(.28f, 1, arrival) * fade;
                    Color color = SpellColor(entry.Spell);
                    visual.Root.transform.position = entry.Position;
                    visual.Root.transform.localScale = Vector3.one * Mathf.Lerp(.65f, 1, arrival) * pulse * .84f;
                    visual.Halo.color = new Color(color.r, color.g, color.b, .22f * alpha);
                    for (int type = 1; type < visual.TypeRoot.Length; type++)
                    {
                        var root = visual.TypeRoot[type];
                        if (root == null) continue;
                        bool active = (int)entry.Spell == type;
                        root.SetActive(active);
                        if (!active) continue;
                        var strokes = visual.Strokes[type];
                        for (int s = 0; s < strokes.Length; s++)
                        {
                            if (strokes[s] == null) continue;
                            Color stroke = strokes[s].color;
                            stroke.a = alpha;
                            strokes[s].color = stroke;
                        }
                    }
                }
            }
            if (ballSpellRoot == null || ball == null) return;
            bool spellVisible = IsVersus && versus.ActiveSpell != RallySpell.None;
            ballSpellRoot.SetActive(spellVisible);
            if (!spellVisible) return;
            ballSpellRoot.transform.position = ball.transform.position;
            Color activeColor = SpellColor(versus.ActiveSpell);
            ballSpellHalo.color = new Color(activeColor.r, activeColor.g, activeColor.b, .20f);
            for (int type = 1; type < ballSpellTypeRoot.Length; type++)
            {
                bool active = (int)versus.ActiveSpell == type;
                if (ballSpellTypeRoot[type] != null) ballSpellTypeRoot[type].SetActive(active);
            }
            bool warning = versus.ActiveSpell == RallySpell.Light && LightPhase == RallyLightPhase.Warning;
            for (int i = 0; i < lightWarningRing.Length; i++)
            {
                var ring = lightWarningRing[i];
                if (ring == null) continue;
                ring.enabled = warning;
                if (!warning) continue;
                Color color = SpellColor(RallySpell.Light);
                color.a = .72f;
                ring.color = color;
            }
            float warningScale = warning
                ? Mathf.Lerp(.55f, 1.16f, Mathf.Clamp01(LightPhaseSecondsRemaining / RallyVersusState.LightWarningSeconds))
                : 1f;
            ballSpellRoot.transform.localScale = Vector3.one * warningScale;
        }

        void CleanupVersusArtwork()
        {
            Release(versusPickupRoot);
            Release(ballSpellRoot);
            Release(versusLeftFloor != null ? versusLeftFloor.gameObject : null);
            Release(versusRightFloor != null ? versusRightFloor.gameObject : null);
            versusPickupRoot = null;
            ballSpellRoot = null;
            versusLeftFloor = versusRightFloor = null;
            for (int i = 0; i < pickupVisuals.Length; i++) pickupVisuals[i] = null;
            for (int i = 0; i < lightWarningRing.Length; i++) lightWarningRing[i] = null;
            for (int i = 1; i < generatedSpellTextures.Length; i++)
            {
                Release(generatedSpellTextures[i]);
                generatedSpellTextures[i] = null;
                spellArtwork[i] = null;
            }
        }
    }
}
