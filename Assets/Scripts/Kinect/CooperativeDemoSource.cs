using UnityEngine;
using UnityEngine.InputSystem;
using Naispo.Silhouette;

namespace Naispo.Kinect
{
    // Local two-player controls feed exactly the same hand-frame path as the sensors.
    public sealed class CooperativeDemoSource : BodyTrackingSource
    {
        public int playerSlot;
        public KinectViewportMapper mapper;
        public bool acceptControls = true;
        public float horizontal = 160, vertical = 207, angle, span = 130;
        long sequence;
        double nextFrame;
        public override string Status => "Keyboard demo";
        public override float CaptureFps => 30;
        public override bool WorkerAlive => isActiveAndEnabled;
        public override bool HasFailed => false;

        void Start() { angle = playerSlot == 0 ? -5 : 5; }

        public override bool TryGetLatest(ref BodyFrameData frame)
        {
            if (!isActiveAndEnabled) return false;
            if (acceptControls) ReadControls();
            if(Time.unscaledTimeAsDouble<nextFrame)return false;
            nextFrame=Time.unscaledTimeAsDouble+1.0/30;
            frame.EnsureSize(320, 288);
            frame.Sequence = ++sequence;
            frame.TimestampSeconds = Time.unscaledTimeAsDouble;
            frame.BodyCount = 1;
            frame.BodyIds[0] = (uint)(100 + playerSlot);
            frame.HasColor = false;
            var direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), -Mathf.Sin(angle * Mathf.Deg2Rad));
            var middle = new Vector2(horizontal, vertical);
            var left = middle - direction * span * .5f;
            var right = middle + direction * span * .5f;
            frame.Hands[0] = new BodyFrameData.HandPair {
                Left = new System.Numerics.Vector2(left.x, left.y),
                Right = new System.Numerics.Vector2(right.x, right.y),
                SeparationMeters = Mathf.Lerp(.35f, 1.45f, Mathf.InverseLerp(65, 175, span)), Valid = true
            };
            // Clearly labelled demo figures, with arms reaching the actual collision endpoints.
            var head = new Vector2(horizontal, 132);
            var neck = new Vector2(horizontal, 158);
            for (int y=0; y<288; y++) for (int x=0; x<320; x++) {
                var p = new Vector2(x,y);
                bool body = (p-head).sqrMagnitude < 16*16 ||
                    (x > horizontal-15 && x < horizontal+15 && y>151 && y<238) ||
                    SquaredDistanceToSegment(p, neck, left)<36 || SquaredDistanceToSegment(p, neck, right)<36 ||
                    SquaredDistanceToSegment(p,new Vector2(horizontal-8,230),new Vector2(horizontal-22,279))<49 ||
                    SquaredDistanceToSegment(p,new Vector2(horizontal+8,230),new Vector2(horizontal+22,279))<49;
                frame.BodyIndexMap[y*320+x] = body ? (byte)0 : (byte)255;
            }
            return true;
        }

        static float SquaredDistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var d=b-a; float t=Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.001f,d.sqrMagnitude));
            return (p-a-t*d).sqrMagnitude;
        }

        void ReadControls()
        {
            var k=Keyboard.current; float dt=Mathf.Min(Time.unscaledDeltaTime,.05f);
            if (k!=null) {
                if(playerSlot==0) {
                    horizontal += ((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0))*90*dt;
                    vertical += ((k.sKey.isPressed?1:0)-(k.wKey.isPressed?1:0))*75*dt;
                    angle += ((k.qKey.isPressed?1:0)-(k.eKey.isPressed?1:0))*60*dt;
                    span += ((k.xKey.isPressed?1:0)-(k.zKey.isPressed?1:0))*70*dt;
                } else {
                    horizontal += ((k.rightArrowKey.isPressed?1:0)-(k.leftArrowKey.isPressed?1:0))*90*dt;
                    vertical += ((k.downArrowKey.isPressed?1:0)-(k.upArrowKey.isPressed?1:0))*75*dt;
                    angle += ((k.jKey.isPressed?1:0)-(k.lKey.isPressed?1:0))*60*dt;
                    span += ((k.oKey.isPressed?1:0)-(k.uKey.isPressed?1:0))*70*dt;
                }
            }
            var mouse=Mouse.current;
            if(mouse!=null && mouse.leftButton.isPressed && mapper!=null) {
                var pos=mouse.position.ReadValue();
                if(pos.y>Screen.height*.17f && pos.y<Screen.height*.80f && (pos.x<Screen.width*.5f)==(playerSlot==0)) {
                    var world=mapper.targetCamera.ScreenToWorldPoint(new Vector3(pos.x,pos.y,10));
                    var pixel=mapper.WorldToPixel(world);
                    horizontal=pixel.x;vertical=pixel.y;
                }
            }
            angle=Mathf.Clamp(angle,-48,48);span=Mathf.Clamp(span,65,175);
            horizontal=Mathf.Clamp(horizontal,span*.5f+8,312-span*.5f);
            float halfHeight=Mathf.Abs(Mathf.Sin(angle*Mathf.Deg2Rad))*span*.5f;
            vertical=Mathf.Clamp(vertical,Mathf.Max(165,8+halfHeight),Mathf.Min(252,279-halfHeight));
        }
    }
}
