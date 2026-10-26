using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Naispo.Interaction
{
    /// <summary>Bounded, visual-only battle feedback. The game owns its clock and suspension.</summary>
    // Explicit editor previews also allocate artwork, so they need Unity's real cleanup callbacks.
    // There is no autonomous Update: ExecuteAlways does not advance this effect's clock.
    [ExecuteAlways]
    public sealed class RallyBattleEffects : MonoBehaviour
    {
        const int ParticleCapacity = 96, RingCapacity = 12, AudioCapacity = 4;
        const int RingSegments = 48;
        readonly List<Object> resources = new List<Object>();
        readonly Particle[] particles = new Particle[ParticleCapacity];
        readonly Ring[] rings = new Ring[RingCapacity];
        readonly AudioSource[] voices = new AudioSource[AudioCapacity];
        readonly bool[] pausedVoices = new bool[AudioCapacity];
        readonly AudioClip[] sounds = new AudioClip[4];
        GameObject artwork;
        Sprite whiteSprite;
        Material lineMaterial;
        AudioListener listener;
        Vector2 courtCenter;
        float halfWidth, floorY, victoryAge = -1;
        int nextParticle, nextRing, nextVoice, victoryStage, victorySide;
        uint noise = 0xC0FFEEu;
        bool visible = true, suspended;

        sealed class Particle
        {
            public SpriteRenderer View;
            public Vector2 Origin, Velocity, Acceleration, StartSize, EndSize;
            public Color Color;
            public float Age, Life, Angle, Spin;
            public bool Active;
        }

        sealed class Ring
        {
            public LineRenderer View;
            public Vector2 Origin, StartSize, EndSize;
            public Color Color;
            public float Age, Life, Width;
            public bool Active;
        }

        public float EffectAge { get; private set; }
        public bool IsVictory => victoryAge >= 0;
        public int ActiveParticleCount
        {
            get
            {
                int count = 0;
                for(int i = 0; i < particles.Length; i++) if(particles[i] != null && particles[i].Active) count++;
                for(int i = 0; i < rings.Length; i++) if(rings[i] != null && rings[i].Active) count++;
                return count;
            }
        }

        public void Initialize(Camera camera, float courtHalfWidth, float courtFloorY, Vector2 center)
        {
            if(artwork != null || camera == null || !camera.orthographic) return;
            halfWidth = Mathf.Max(1, courtHalfWidth);
            floorY = courtFloorY;
            courtCenter = center;
            artwork = Child("Battle impact effects", transform);
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Battle effect white" };
            texture.SetPixel(0, 0, Color.white); texture.Apply(false, true);
            resources.Add(texture);
            whiteSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * .5f, 1);
            whiteSprite.name = "Battle effect shard";
            resources.Add(whiteSprite);
            lineMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Battle effect rings" };
            resources.Add(lineMaterial);

            // Reserve every renderer once. Bursts recycle the oldest slot instead of allocating.
            for(int i = 0; i < particles.Length; i++)
            {
                var view = Child("Battle shard " + i, artwork.transform).AddComponent<SpriteRenderer>();
                view.sprite = whiteSprite; view.sortingOrder = 12; view.enabled = false;
                particles[i] = new Particle { View = view };
            }
            for(int i = 0; i < rings.Length; i++)
            {
                var view = Child("Battle shock ring " + i, artwork.transform).AddComponent<LineRenderer>();
                view.sharedMaterial = lineMaterial;
                view.useWorldSpace = false; view.loop = true; view.positionCount = RingSegments;
                view.numCornerVertices = 2; view.sortingOrder = 12; view.enabled = false;
                for(int point = 0; point < RingSegments; point++)
                {
                    float angle = point * Mathf.PI * 2 / RingSegments;
                    view.SetPosition(point, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0));
                }
                rings[i] = new Ring { View = view };
            }
            for(int i = 0; i < sounds.Length; i++) sounds[i] = CreateSound(i);
            for(int i = 0; i < voices.Length; i++)
            {
                var voice = Child("Battle sound " + i, artwork.transform).AddComponent<AudioSource>();
                voice.playOnAwake = false; voice.loop = false; voice.spatialBlend = 0;
                voice.dopplerLevel = 0; voice.volume = .16f;
                voices[i] = voice;
            }
            artwork.SetActive(visible);
        }

        public void SetVisible(bool value)
        {
            visible = value;
            if(!value) ResetEffects();
            if(artwork != null) artwork.SetActive(value);
        }

        public void SetSuspended(bool value)
        {
            if(suspended == value) return;
            suspended = value;
            for(int i = 0; i < voices.Length; i++)
            {
                var voice = voices[i];
                if(voice == null) continue;
                if(value)
                {
                    pausedVoices[i] = voice.isPlaying;
                    if(pausedVoices[i]) voice.Pause();
                }
                else if(pausedVoices[i]) { voice.UnPause(); pausedVoices[i] = false; }
            }
        }

        public void ResetEffects()
        {
            EffectAge = 0; victoryAge = -1; victoryStage = 0;
            nextParticle = nextRing = nextVoice = 0;
            for(int i = 0; i < particles.Length; i++)
                if(particles[i] != null) { particles[i].Active = false; particles[i].View.enabled = false; }
            for(int i = 0; i < rings.Length; i++)
                if(rings[i] != null) { rings[i].Active = false; rings[i].View.enabled = false; }
            for(int i = 0; i < voices.Length; i++)
            {
                if(voices[i] != null) voices[i].Stop();
                pausedVoices[i] = false;
            }
        }

        public void Step(float dt)
        {
            if(!CanEmit || !(dt > 0) || float.IsInfinity(dt)) return;
            EffectAge += dt;
            // Small steps preserve scheduled bursts even after a slow frame. Nothing survives ten seconds.
            float remaining = Mathf.Min(dt, 10);
            while(remaining > 0)
            {
                float step = Mathf.Min(.05f, remaining);
                Advance(step);
                remaining -= step;
            }
        }

        public void Hit(Vector2 position, int side, float strength01)
        {
            if(!CanEmit || !Finite(position)) return;
            float strength = float.IsNaN(strength01) ? 0 : Mathf.Clamp01(strength01);
            Color color = SideColor(side);
            ShockRing(position, Vector2.one * .20f, Vector2.one * Mathf.Lerp(.65f, 1.05f, strength),
                .035f, .25f, color);
            RadialBurst(position, color, 8 + Mathf.RoundToInt(strength * 6), 2.4f + strength * 2, .28f, .04f);
            PlaySound(0, Mathf.Lerp(.08f, .14f, strength));
        }

        public void Pickup(Vector2 position, int side)
        {
            if(!CanEmit || !Finite(position)) return;
            ShockRing(position, Vector2.one * .18f, Vector2.one * .85f, .035f, .4f,
                new Color(.76f, .46f, 1));
            RadialBurst(position, SideColor(side), 12, 2.6f, .43f, .04f);
            PlaySound(1, .12f);
        }

        public void Goal(Vector2 position, int winner, bool matchWon)
        {
            if(!CanEmit || !Finite(position)) return;
            Color color = SideColor(winner);
            Vector2 origin = new Vector2(Mathf.Clamp(position.x, courtCenter.x - halfWidth + .3f,
                courtCenter.x + halfWidth - .3f), floorY + .04f);
            ShockRing(origin, new Vector2(.22f, .07f), new Vector2(halfWidth * .65f, .48f), .055f, .72f, color);
            ShockRing(origin, new Vector2(.15f, .04f), new Vector2(halfWidth * .52f, .34f), .025f, .75f, color, .12f);
            for(int i = 0; i < (matchWon ? 34 : 26); i++)
            {
                float x = Next(-1, 1);
                Vector2 velocity = new Vector2(x * 4.5f, Next(2, 6));
                Fragment(origin, velocity, new Vector2(0, -6), new Vector2(Next(.08f, .20f), .045f),
                    new Vector2(.025f, .015f), Next(.6f, 1.1f), color, Next(0, 180), Next(-260, 260));
            }
            for(int i = -2; i <= 2; i++)
            {
                float height = 2.7f - Mathf.Abs(i) * .6f;
                Vector2 size = new Vector2(i == 0 ? .075f : .035f, height);
                Color beam = color; beam.a = i == 0 ? .55f : .32f;
                Fragment(origin + new Vector2(i * .23f, height * .5f), Vector2.up * .15f,
                    Vector2.zero, size, new Vector2(.005f, height * 1.25f), .48f, beam, 0, 0);
            }
            if(matchWon)
            {
                victorySide = winner < 0 ? -1 : 1; victoryAge = 0; victoryStage = 0;
                VictoryBurst(0);
            }
            else victoryAge = -1;
            PlaySound(matchWon ? 3 : 2, matchWon ? .19f : .17f);
        }

        bool CanEmit => artwork != null && visible && !suspended && isActiveAndEnabled;

        void Advance(float dt)
        {
            for(int i = 0; i < particles.Length; i++)
            {
                var particle = particles[i];
                if(!particle.Active) continue;
                particle.Age += dt;
                Draw(particle);
            }
            for(int i = 0; i < rings.Length; i++)
            {
                var ring = rings[i];
                if(!ring.Active) continue;
                ring.Age += dt;
                Draw(ring);
            }
            if(victoryAge < 0) return;
            victoryAge += dt;
            while(victoryStage < 3 && victoryAge >= VictoryDelay(victoryStage))
            {
                VictoryBurst(victoryStage + 1); victoryStage++;
            }
            if(victoryAge >= 2.8f) victoryAge = -1;
        }

        static float VictoryDelay(int stage) => stage == 0 ? .35f : stage == 1 ? .9f : 1.55f;

        void VictoryBurst(int stage)
        {
            float x = courtCenter.x + victorySide * halfWidth * (.32f + (stage % 2) * .19f);
            float y = courtCenter.y + .35f + (stage % 3) * .65f;
            var position = new Vector2(x, y);
            Color color = SideColor(victorySide);
            ShockRing(position, Vector2.one * .2f, Vector2.one * (1.2f + stage * .18f),
                .035f, .7f, color);
            RadialBurst(position, color, 20, 3.2f, .9f, .075f);
        }

        void RadialBurst(Vector2 position, Color color, int count, float speed, float life, float width)
        {
            float offset = Next(0, Mathf.PI * 2);
            for(int i = 0; i < count; i++)
            {
                float angle = offset + i * Mathf.PI * 2 / count + Next(-.12f, .12f);
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Color shard = Color.Lerp(color, Color.white, i % 3 == 0 ? .35f : 0);
                Fragment(position + direction * .24f, direction * speed * Next(.65f, 1),
                    Vector2.zero, new Vector2(Next(.12f, .26f), width), new Vector2(.04f, width * .25f),
                    life * Next(.75f, 1.15f), shard, angle * Mathf.Rad2Deg, 0);
            }
        }

        void Fragment(Vector2 position, Vector2 velocity, Vector2 acceleration, Vector2 startSize,
            Vector2 endSize, float life, Color color, float angle, float spin)
        {
            var particle = particles[nextParticle]; nextParticle = (nextParticle + 1) % particles.Length;
            particle.Origin = position; particle.Velocity = velocity; particle.Acceleration = acceleration;
            particle.StartSize = startSize; particle.EndSize = endSize; particle.Life = life;
            particle.Color = color; particle.Angle = angle; particle.Spin = spin;
            particle.Age = 0; particle.Active = true;
            Draw(particle);
        }

        void ShockRing(Vector2 position, Vector2 startSize, Vector2 endSize, float width,
            float life, Color color, float delay = 0)
        {
            var ring = rings[nextRing]; nextRing = (nextRing + 1) % rings.Length;
            ring.Origin = position; ring.StartSize = startSize; ring.EndSize = endSize;
            ring.Width = width; ring.Life = life; ring.Color = color;
            ring.Age = -delay; ring.Active = true;
            Draw(ring);
        }

        static void Draw(Particle particle)
        {
            particle.Active = particle.Age < particle.Life;
            particle.View.enabled = particle.Active;
            if(!particle.Active) return;
            float amount = particle.Age / particle.Life;
            Vector2 position = particle.Origin + particle.Velocity * particle.Age
                + particle.Acceleration * (particle.Age * particle.Age * .5f);
            particle.View.transform.position = new Vector3(position.x, position.y, 0);
            Vector2 size = Vector2.Lerp(particle.StartSize, particle.EndSize, amount);
            particle.View.transform.localScale = new Vector3(size.x, size.y, 1);
            particle.View.transform.rotation = Quaternion.Euler(0, 0, particle.Angle + particle.Spin * particle.Age);
            Color color = particle.Color;
            color.a *= 1 - amount * amount;
            particle.View.color = color;
        }

        static void Draw(Ring ring)
        {
            ring.Active = ring.Age < ring.Life;
            ring.View.enabled = ring.Active && ring.Age >= 0;
            if(!ring.View.enabled) return;
            float amount = ring.Age / ring.Life;
            float ease = 1 - (1 - amount) * (1 - amount);
            Vector2 size = Vector2.Lerp(ring.StartSize, ring.EndSize, ease);
            ring.View.transform.position = new Vector3(ring.Origin.x, ring.Origin.y, 0);
            ring.View.transform.localScale = new Vector3(size.x, size.y, 1);
            ring.View.startWidth = ring.View.endWidth = ring.Width * (1 - amount * .8f);
            Color color = ring.Color; color.a *= (1 - amount) * .8f;
            ring.View.startColor = ring.View.endColor = color;
        }

        float Next(float minimum, float maximum)
        {
            // Local noise leaves gameplay's random stream untouched.
            noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
            return Mathf.Lerp(minimum, maximum, (noise & 0xFFFFFF) / 16777215f);
        }

        static Color SideColor(int side) => side < 0 ? new Color(.64f, .94f, .80f)
            : new Color(.94f, .76f, .49f);
        static bool Finite(Vector2 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y)
            && !float.IsInfinity(value.x) && !float.IsInfinity(value.y);

        void PlaySound(int index, float volume)
        {
            if(!Application.isPlaying) return;
            if(listener == null || !listener.isActiveAndEnabled)
            {
                listener = null;
                foreach(var candidate in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if(candidate.isActiveAndEnabled) { listener = candidate; break; }
            }
            if(listener == null || !listener.isActiveAndEnabled) return;
            var voice = voices[nextVoice];
            pausedVoices[nextVoice] = false;
            nextVoice = (nextVoice + 1) % voices.Length;
            voice.Stop(); voice.clip = sounds[index]; voice.volume = volume; voice.Play();
        }

        AudioClip CreateSound(int kind)
        {
            const int sampleRate = 22050;
            float duration = kind == 0 ? .18f : kind == 1 ? .36f : kind == 2 ? .78f : 1.5f;
            var samples = new float[Mathf.CeilToInt(duration * sampleRate)];
            for(int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)sampleRate;
                float sample;
                if(kind == 0)
                    sample = Tone(time, 0, 100, 28) * .7f + Tone(time, 0, 1050, 45) * .25f;
                else if(kind == 1)
                    sample = Tone(time, 0, 660, 18) * .4f + Tone(time, .07f, 990, 16) * .5f;
                else if(kind == 2)
                    sample = Tone(time, 0, 392, 12) * .35f + Tone(time, .07f, 523.25f, 12) * .35f
                        + Tone(time, .14f, 659.25f, 10) * .35f + Tone(time, .21f, 784, 8) * .4f;
                else
                    sample = Tone(time, 0, 261.63f, 3) * .22f + Tone(time, .04f, 329.63f, 3) * .22f
                        + Tone(time, .08f, 392, 3) * .22f + Tone(time, .12f, 523.25f, 3) * .25f
                        + Tone(time, .24f, 784, 5) * .20f;
                samples[i] = sample * Mathf.Clamp01((duration - time) / .025f);
            }
            var clip = AudioClip.Create("Battle feedback " + kind, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0); resources.Add(clip);
            return clip;
        }

        static float Tone(float time, float start, float frequency, float decay)
        {
            float age = time - start;
            if(age < 0) return 0;
            return Mathf.Sin(age * frequency * Mathf.PI * 2) * Mathf.Exp(-age * decay) * Mathf.Clamp01(age / .004f);
        }

        GameObject Child(string childName, Transform parent)
        {
            var child = new GameObject(childName);
            SceneManager.MoveGameObjectToScene(child, gameObject.scene);
            child.transform.SetParent(parent, false);
            return child;
        }

        void OnDisable() => ResetEffects();
        void OnDestroy()
        {
            Release(artwork);
            for(int i = resources.Count - 1; i >= 0; i--) Release(resources[i]);
            resources.Clear();
        }
        static void Release(Object resource)
        {
            if(resource == null) return;
            if(Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
        }
    }
}
