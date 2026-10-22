using UnityEngine;

namespace Naispo.Interaction
{
    // A short lost-hand grace and a sample-driven recovery gate share one pose.
    // Re-reading a cached camera frame never extends either timer.
    public sealed class HandPoseStabilizer
    {
        public bool Ready { get; private set; }
        public bool Holding => Ready && !latestValid;
        public bool Acquiring => !Ready && consecutiveSamples>0;
        public Vector2 Left { get; private set; }
        public Vector2 Right { get; private set; }
        public float SeparationMeters { get; private set; }
        public uint Owner { get; private set; }
        bool hasOwner,latestValid;
        long lastRevision=long.MinValue;
        double lastValidAt=double.NegativeInfinity,firstGoodAt;
        int consecutiveSamples;

        public void Reset()
        {
            Ready=hasOwner=latestValid=false;lastRevision=long.MinValue;
            lastValidAt=double.NegativeInfinity;firstGoodAt=0;consecutiveSamples=0;
            Left=Right=Vector2.zero;SeparationMeters=0;Owner=0;
        }

        public void Observe(long revision,uint? identity,bool valid,Vector2 left,Vector2 right,float meters,
            double receivedAt,double now,float grace=.25f,float recovery=.20f)
        {
            Advance(now,grace);
            if(revision==lastRevision)return;
            if(identity.HasValue&&hasOwner&&Owner!=identity.Value)Reset();
            lastRevision=revision;
            if(identity.HasValue) { Owner=identity.Value;hasOwner=true; }
            latestValid=valid&&identity.HasValue&&now-receivedAt<=grace;
            if(!latestValid) { consecutiveSamples=0;return; }
            // The receipt timestamp, rather than this physics tick, bounds cached input.
            if(consecutiveSamples==0||receivedAt-lastValidAt>grace)
            { firstGoodAt=receivedAt;consecutiveSamples=0; }
            consecutiveSamples++;
            lastValidAt=receivedAt;
            Left=left;Right=right;SeparationMeters=meters;
            if(!Ready&&(recovery<=0||(consecutiveSamples>=2&&receivedAt-firstGoodAt>=recovery-.000001)))Ready=true;
        }

        public void Advance(double now,float grace=.25f)
        {
            if(now-lastValidAt<=grace)return;
            Ready=latestValid=false;consecutiveSamples=0;
        }
    }
}
