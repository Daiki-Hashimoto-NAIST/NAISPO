using NUnit.Framework;
using Naispo.Kinect;

namespace Naispo.Tests
{
    public class BallLightTests
    {
        [Test] public void ColorAndMaskTravelTogetherWithoutOverwritingConsumer()
        {
            var buffer=new LatestBodyFrameBuffer();
            var producer=new BodyFrameData(); var consumer=new BodyFrameData();
            producer.EnsureSize(4,3); producer.Sequence=1; producer.HasColor=true;
            producer.BodyIndexMap[0]=2; producer.AlignedColorBgra[0]=123;
            buffer.Publish(ref producer); Assert.That(buffer.TryTake(ref consumer),Is.True);
            producer.EnsureSize(4,3); producer.Sequence=2; producer.HasColor=false;
            producer.AlignedColorBgra[0]=45; buffer.Publish(ref producer);
            Assert.That(consumer.Sequence,Is.EqualTo(1)); Assert.That(consumer.AlignedColorBgra[0],Is.EqualTo(123));
            Assert.That(consumer.BodyIndexMap[0],Is.EqualTo(2)); Assert.That(consumer.HasColor,Is.True);
            Assert.That(buffer.TryTake(ref consumer),Is.True); Assert.That(consumer.HasColor,Is.False);
        }
        [Test] public void ResolutionChangeResetsColorValidityAndAllocatesMatchingImage()
        {
            var frame=new BodyFrameData(); frame.EnsureSize(320,288);frame.HasColor=true;
            frame.EnsureSize(640,576);
            Assert.That(frame.AlignedColorBgra.Length,Is.EqualTo(640*576*4));
            Assert.That(frame.HasColor,Is.False);
        }
    }
}
