using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Naispo.Interaction
{
    /// <summary>Short spell traces, driven exclusively by the game's presentation clock.</summary>
    [ExecuteAlways]
    public sealed class RallySpellBallEffects : MonoBehaviour
    {
        const int FireCapacity = 24, EchoCapacity = 3, SparkCapacity = 8;
        const float EchoInterval = .055f;
        readonly List<Object> resources = new List<Object>();
        readonly Particle[] wisps = new Particle[FireCapacity];
        readonly Particle[] echoes = new Particle[EchoCapacity];
        readonly Particle[] sparks = new Particle[SparkCapacity];
        readonly SpriteRenderer[] tongues = new SpriteRenderer[3];
        GameObject artwork;
        Sprite flameSprite, sparkSprite;
        SpriteRenderer ring;
        RallySpell activeSpell;
        Vector2 previousPosition, ringOrigin;
        float emissionClock, ringAge, ghostScale = 1;
        Vector2 ghostCenter;
        int nextWisp, nextEcho;
        bool visible = true, suspended, hidden, ringActive;
        uint noise = 0x5A11B411u;

        sealed class Particle
        {
            public SpriteRenderer View;
            public Vector2 Origin, Velocity, StartSize, EndSize;
            public Color Color;
            public float Age, Life, Angle, Spin;
            public bool Active;
        }

        public float EffectAge { get; private set; }
        public int ActiveParticleCount => Count(wisps) + Count(echoes) + Count(sparks) + (ringActive ? 1 : 0);

        /// <param name="ballSprite">Borrowed artwork; its texture, crop and pivot are never modified.</param>
        public void Initialize(Sprite ballSprite)
        {
            if (artwork != null) return;
            artwork = Child("Spell ball effects", transform);
            flameSprite = MakeSprite("Spell flame", 96, 128, 0);
            sparkSprite = MakeSprite("Spell vanish diamond", 64, 64, 1);
            var ringSprite = MakeSprite("Spell vanish circle", 128, 128, 2);
            if (ballSprite != null)
            {
                float extent = Mathf.Max(ballSprite.bounds.size.x, ballSprite.bounds.size.y);
                ghostScale = extent > .0001f ? .44f / extent : 1;
                ghostCenter = ballSprite.bounds.center;
            }
            for (int i = 0; i < tongues.Length; i++)
                tongues[i] = Renderer("Fire tongue " + i, flameSprite, 19);
            for (int i = 0; i < wisps.Length; i++)
                wisps[i] = new Particle { View = Renderer("Fire wisp " + i, flameSprite, 18) };
            for (int i = 0; i < echoes.Length; i++)
                echoes[i] = new Particle { View = Renderer("Clock echo " + i, ballSprite, 18) };
            for (int i = 0; i < sparks.Length; i++)
                sparks[i] = new Particle { View = Renderer("Vanish spark " + i, sparkSprite, 21) };
            ring = Renderer("Vanish ring", ringSprite, 21);
            artwork.SetActive(visible && isActiveAndEnabled);
        }

        public void SetState(RallySpell spell, Vector2 position, Vector2 velocity,
            bool visible, bool suspended, bool hidden)
        {
            this.visible = visible;
            this.suspended = suspended;
            if (!visible)
            {
                ResetEffects();
                if (artwork != null) artwork.SetActive(false);
                return;
            }
            if (artwork != null) artwork.SetActive(isActiveAndEnabled);
            if (!Finite(position) || !Finite(velocity)) return;
            if (activeSpell != spell || this.hidden != hidden)
            {
                ClearMoving();
                previousPosition = position;
                emissionClock = 0;
            }
            activeSpell = spell;
            this.hidden = hidden;
            if (hidden || spell != RallySpell.Fireball) HideTongues();
            else if (CanAdvance) DrawTongues(position, velocity);
        }

        public void Step(float dt, Vector2 position, Vector2 velocity)
        {
            if (!CanAdvance || !(dt > 0) || float.IsInfinity(dt) || !Finite(position) || !Finite(velocity)) return;
            EffectAge += dt;
            Advance(wisps, dt);
            Advance(echoes, dt);
            Advance(sparks, dt);
            if (ringActive) { ringAge += dt; DrawRing(); }
            if (hidden) { previousPosition = position; return; }
            if (activeSpell != RallySpell.Fireball && activeSpell != RallySpell.Timer)
            { previousPosition = position; return; }

            float speed = velocity.magnitude;
            // A serve, recalibration or resumed position must not draw a streak across the court.
            if (Vector2.Distance(previousPosition, position) > Mathf.Max(1.5f, speed * dt * 1.8f))
            {
                ClearMoving();
                previousPosition = position;
                emissionClock = 0;
            }
            float interval = activeSpell == RallySpell.Timer ? EchoInterval : Mathf.Min(.035f, .25f / Mathf.Max(speed, 1));
            emissionClock += dt;
            int count = Mathf.Min(4, Mathf.FloorToInt(emissionClock / interval));
            float remainder = emissionClock % interval;
            if (count > 0)
            {
                for (int i = count - 1; i >= 0; i--)
                {
                    float age = remainder + i * interval;
                    Vector2 sample = Vector2.Lerp(previousPosition, position, Mathf.Clamp01(1 - age / dt));
                    if (activeSpell == RallySpell.Fireball) EmitFire(sample, velocity, age);
                    else EmitEcho(sample, age);
                }
                emissionClock = remainder;
            }
            previousPosition = position;
            if (activeSpell == RallySpell.Fireball) DrawTongues(position, velocity);
        }

        // Only the warning-to-hidden transition calls this. Every particle keeps this origin;
        // the concealed ball's later coordinates cannot be inferred from the burst.
        public void Vanish(Vector2 position)
        {
            if (!CanAdvance || !Finite(position)) return;
            ClearMoving();
            HideTongues();
            hidden = true;
            for (int i = 0; i < sparks.Length; i++)
            {
                float angle = i * Mathf.PI * 2 / sparks.Length + .18f;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Color color = i % 2 == 0 ? new Color(.86f, .67f, 1, .88f) : new Color(1, .91f, 1, .8f);
                Emit(sparks[i], position + direction * .09f, direction * 1.4f,
                    new Vector2(.16f, .045f), new Vector2(.035f, .012f), .19f, 0,
                    color, angle * Mathf.Rad2Deg, 0);
            }
            ringOrigin = position;
            ringAge = 0;
            ringActive = true;
            DrawRing();
        }

        public void ResetEffects()
        {
            EffectAge = 0;
            activeSpell = RallySpell.None;
            hidden = false;
            emissionClock = 0;
            nextWisp = nextEcho = 0;
            ClearMoving();
            Clear(sparks);
            HideTongues();
            ringActive = false;
            if (ring != null) ring.enabled = false;
        }

        bool CanAdvance => artwork != null && visible && !suspended && isActiveAndEnabled;

        void EmitFire(Vector2 position, Vector2 velocity, float age)
        {
            float speed = velocity.magnitude;
            float life = Mathf.Min(.21f, .8f / Mathf.Max(speed, 1));
            if (age >= life) return;
            Vector2 tail = speed > .2f ? -velocity / speed : Vector2.up;
            Vector2 across = new Vector2(-tail.y, tail.x);
            var particle = wisps[nextWisp];
            nextWisp = (nextWisp + 1) % wisps.Length;
            float width = Next(.12f, .19f);
            Emit(particle, position + tail * .12f + across * Next(-.07f, .07f),
                Vector2.up * .2f + tail * .15f,
                new Vector2(width, Next(.23f, .34f)), new Vector2(.025f, .10f), life, age,
                new Color(1, Next(.65f, .84f), .46f, .52f),
                Mathf.Atan2(tail.y, tail.x) * Mathf.Rad2Deg - 90 + Next(-12, 12), Next(-30, 30));
        }

        void EmitEcho(Vector2 position, float age)
        {
            var particle = echoes[nextEcho];
            nextEcho = (nextEcho + 1) % echoes.Length;
            if (particle.View.sprite == null) return;
            Vector2 scale = Vector2.one * ghostScale;
            Emit(particle, position - ghostCenter * ghostScale, Vector2.zero, scale, scale,
                .18f, age, new Color(.25f, .80f, 1, .23f), 0, 0);
        }

        void DrawTongues(Vector2 position, Vector2 velocity)
        {
            float speed = velocity.magnitude;
            Vector2 tail = speed > .2f ? -velocity / speed : Vector2.up;
            Vector2 across = new Vector2(-tail.y, tail.x);
            float angle = Mathf.Atan2(tail.y, tail.x) * Mathf.Rad2Deg - 90;
            for (int i = 0; i < tongues.Length; i++)
            {
                var view = tongues[i];
                if (view == null) continue;
                float side = i - 1;
                float pulse = Mathf.Sin(EffectAge * (19 + i * 3) + i * 2.1f);
                Vector2 center = position + tail * (.13f + (i == 1 ? .05f : 0)) + across * side * .15f;
                view.transform.position = new Vector3(center.x, center.y, 0);
                view.transform.rotation = Quaternion.Euler(0, 0, angle - side * 20 + pulse * 7);
                view.transform.localScale = new Vector3(i == 1 ? .48f : .38f, (i == 1 ? .72f : .55f) + pulse * .035f, 1);
                view.color = new Color(1, i == 1 ? .95f : .78f, .7f, i == 1 ? .92f : .75f);
                view.enabled = true;
            }
        }

        void DrawRing()
        {
            ringActive = ringAge < .17f;
            ring.enabled = ringActive;
            if (!ringActive) return;
            float amount = ringAge / .17f;
            ring.transform.position = new Vector3(ringOrigin.x, ringOrigin.y, 0);
            ring.transform.localScale = Vector3.one * Mathf.Lerp(.30f, .82f, amount);
            ring.color = new Color(.78f, .52f, 1, .58f * (1 - amount) * (1 - amount));
        }

        static void Emit(Particle particle, Vector2 origin, Vector2 velocity, Vector2 startSize,
            Vector2 endSize, float life, float age, Color color, float angle, float spin)
        {
            particle.Origin = origin;
            particle.Velocity = velocity;
            particle.StartSize = startSize;
            particle.EndSize = endSize;
            particle.Life = life;
            particle.Age = age;
            particle.Color = color;
            particle.Angle = angle;
            particle.Spin = spin;
            particle.Active = age < life;
            Draw(particle);
        }

        static void Advance(Particle[] particles, float dt)
        {
            for (int i = 0; i < particles.Length; i++)
            {
                var particle = particles[i];
                if (particle == null || !particle.Active) continue;
                particle.Age += dt;
                Draw(particle);
            }
        }

        static void Draw(Particle particle)
        {
            particle.Active = particle.Age < particle.Life;
            particle.View.enabled = particle.Active;
            if (!particle.Active) return;
            float amount = Mathf.Clamp01(particle.Age / particle.Life);
            Vector2 position = particle.Origin + particle.Velocity * particle.Age;
            particle.View.transform.position = new Vector3(position.x, position.y, 0);
            Vector2 size = Vector2.Lerp(particle.StartSize, particle.EndSize, amount);
            particle.View.transform.localScale = new Vector3(size.x, size.y, 1);
            particle.View.transform.rotation = Quaternion.Euler(0, 0, particle.Angle + particle.Spin * particle.Age);
            Color color = particle.Color;
            color.a *= (1 - amount) * (1 - amount);
            particle.View.color = color;
        }

        void ClearMoving() { Clear(wisps); Clear(echoes); }
        void HideTongues()
        {
            for (int i = 0; i < tongues.Length; i++)
                if (tongues[i] != null) tongues[i].enabled = false;
        }
        static void Clear(Particle[] particles)
        {
            for (int i = 0; i < particles.Length; i++)
                if (particles[i] != null) { particles[i].Active = false; particles[i].View.enabled = false; }
        }
        static int Count(Particle[] particles)
        {
            int count = 0;
            for (int i = 0; i < particles.Length; i++)
                if (particles[i] != null && particles[i].Active) count++;
            return count;
        }

        Sprite MakeSprite(string name, int width, int height, int shape)
        {
            var pixels = new Color32[width * height];
            float aa = 1.5f / Mathf.Min(width, height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float px = (x + .5f) / width - .5f;
                    float py = (y + .5f) / height - .5f;
                    Color color = Color.white;
                    float distance;
                    if (shape == 0)
                    {
                        float t = Mathf.Clamp01((py + .46f) / .92f);
                        float center = .17f * Mathf.Sin(t * 4.8f) * t;
                        float extent = .35f * Mathf.Pow(Mathf.Sin(t * Mathf.PI), .72f) * (1 - .50f * t);
                        distance = Mathf.Max(Mathf.Abs(px - center) - extent, Mathf.Abs(py) - .46f);
                        float inner = Mathf.Clamp01((extent * .53f - Mathf.Abs(px - center + .035f)) / .07f) * (1 - t);
                        color = Color.Lerp(new Color(1, .42f + .17f * (1 - t), .08f), new Color(1, .94f, .62f), inner);
                    }
                    else if (shape == 1)
                        distance = (Mathf.Abs(px) + Mathf.Abs(py) - .47f) * .7071f;
                    else
                        distance = Mathf.Abs(Mathf.Sqrt(px * px + py * py) - .44f) - .013f;
                    color.a = Mathf.Clamp01(.5f - distance / aa);
                    pixels[y * width + x] = color;
                }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { name = name + " texture", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            resources.Add(texture);
            // Unit height keeps the authored flame's narrow aspect and the other shapes square.
            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), Vector2.one * .5f, height);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            resources.Add(sprite);
            return sprite;
        }

        SpriteRenderer Renderer(string name, Sprite sprite, int order)
        {
            var view = Child(name, artwork.transform).AddComponent<SpriteRenderer>();
            view.sprite = sprite;
            view.sortingOrder = order;
            view.enabled = false;
            return view;
        }
        GameObject Child(string name, Transform parent)
        {
            var child = new GameObject(name);
            SceneManager.MoveGameObjectToScene(child, gameObject.scene);
            child.transform.SetParent(parent, false);
            return child;
        }
        float Next(float minimum, float maximum)
        {
            noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
            return Mathf.Lerp(minimum, maximum, (noise & 0xFFFFFF) / 16777215f);
        }
        static bool Finite(Vector2 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y)
            && !float.IsInfinity(value.x) && !float.IsInfinity(value.y);

        void OnEnable() { if (artwork != null) artwork.SetActive(visible); }
        void OnDisable() { if (artwork != null) artwork.SetActive(false); }
        void OnDestroy()
        {
            Release(artwork);
            for (int i = resources.Count - 1; i >= 0; i--) Release(resources[i]);
            resources.Clear();
        }
        static void Release(Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
        }
    }
}
