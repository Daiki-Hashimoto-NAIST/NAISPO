using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Naispo.Interaction;

namespace Naispo.Tests
{
    public sealed class CooperativeStarHintTests
    {
        CooperativeCoinGame game;
        AirHockeyBall ball;
        static readonly int[] RemainingStars = { 2, 6 };
        static readonly Vector2 ParkPosition = new Vector2(0, -2);

        [UnitySetUp]
        public IEnumerator CreateCourt()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            // An unsimulated local physics scene leaves the ball stationary while
            // actual player-loop frames run collection, timing, and artwork.
            var scene = SceneManager.CreateScene("Star hint test",
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            var cameraObject = new GameObject("Test camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.aspect = 16f / 9f;
            var ballObject = new GameObject("Shared ball");
            SceneManager.MoveGameObjectToScene(ballObject, scene);
            ball = ballObject.AddComponent<AirHockeyBall>();
            ball.GetComponent<CircleCollider2D>().radius = .22f;
            ball.Body.interpolation = RigidbodyInterpolation2D.None;
            var gameObject = new GameObject("Cooperative game");
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            game = gameObject.AddComponent<CooperativeCoinGame>();
            game.Configure(ball, camera);
            game.BallLightEnabled = false;
            yield return WaitFor(() => game.CanAdvanceBall, 3, "The initial serve must start.");
        }

        [UnityTest]
        public IEnumerator TwoRemainingStarsTwinkleInTurnsWithoutRevealingOrCollectingThem()
        {
            yield return LeaveTwoStars();
            yield return WaitFor(() => HintWait <= .5f, 9, "Eight seconds of play must approach the first hint.");
            AssertHintHidden("The hint must not appear before the eight-second delay.");
            yield return WaitFor(() => HintVisible, 2, "A stalled search must receive a brief hint.");
            float firstAt = Time.time;
            int firstStar = ClosestRemainingStar();
            AssertHintDoesNotRevealStars();
            Assert.That(Speck.sprite, Is.Not.SameAs(Coin(firstStar).sprite),
                "The hint must be a light particle rather than another copy of the star.");
            Assert.That(Speck.GetComponent<Collider2D>(), Is.Null);
            Assert.That(HintHalo.GetComponent<Collider2D>(), Is.Null);
            Assert.That(Vector2.Distance(Speck.transform.position, Coin(firstStar).transform.position),
                Is.GreaterThan(.1f).And.LessThan(.8f), "The hint should suggest an area, not mark the exact star center.");

            yield return WaitFor(() => !HintVisible, 1.5f, "The light particle must disappear after its short pulse.");
            Assert.That(Time.time - firstAt, Is.LessThan(1.2f));
            AssertHintDoesNotRevealStars();
            yield return WaitFor(() => HintVisible, 5, "A stalled search should receive another occasional hint.");
            Assert.That(Time.time - firstAt, Is.InRange(3.5f, 5.2f));
            int secondStar = ClosestRemainingStar();
            Assert.That(secondStar, Is.Not.EqualTo(firstStar),
                "Two remaining stars should take turns receiving hints.");
            AssertHintDoesNotRevealStars();

            MoveBall((Vector2)Coin(secondStar).transform.position + Vector2.down * game.BallLightRadius * .75f);
            yield return TwoFrames();
            Assert.That(game.CollectedCount, Is.EqualTo(7), "Revealing a star must still require close contact to collect it.");
            Assert.That(IsVisible(Coin(secondStar)), Is.True);
            AssertHintHidden("The hint must disappear as soon as the ball actually illuminates its star.");

            game.RestartGame();
            yield return TwoFrames();
            AssertHintHidden("Restart cannot leave the previous round's hint on screen.");
            AssertFreshWait();
            Assert.That(game.CollectedCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator GameplayGatesFreezeAndHideHintsAndDarknessRestartsTheWait()
        {
            yield return LeaveTwoStars();
            yield return WaitFor(() => HintVisible, 10, "A hint must be active before testing interruption.");

            game.SetPaused(true);
            yield return AssertFrozenHint("Pause");
            game.SetInputReady(false);
            game.SetPaused(false);
            yield return AssertFrozenHint("Tracking wait");
            game.ExternalOverlayOpen = true;
            game.SetInputReady(true);
            yield return AssertFrozenHint("Camera overlay");
            game.ExternalOverlayOpen = false;

            MoveBall(new Vector2(0, game.ArenaFloor - 1));
            yield return WaitFor(() => game.WaitingToServe, 1, "A miss must start a new serve.");
            yield return AssertFrozenHint("Serve countdown");

            game.BallLightEnabled = false;
            yield return TwoFrames();
            AssertHintHidden("Turning off the ball light must also turn off hints.");
            AssertFreshWait();
            yield return new WaitForSecondsRealtime(.2f);
            AssertFreshWait();
            yield return WaitFor(() => game.CanAdvanceBall, 3, "The serve must finish while the light is off.");
            MoveBall(ParkPosition);
            game.BallLightEnabled = true;
            yield return WaitFor(() => HintWait < 7, 2, "Playing with the light on must resume the hint delay.");
            Assert.That(HintWait, Is.GreaterThan(6.5f));
            AssertHintHidden("Turning the light back on must require a fresh eight-second search.");
        }

        [UnityTest]
        public IEnumerator PickupRestartsTheSearchDelayAndModeOrRoundChangesClearHints()
        {
            yield return LeaveTwoStars();
            yield return WaitFor(() => HintVisible, 10, "A remaining star must receive a hint.");
            int picked = ClosestRemainingStar();
            MoveBall(Coin(picked).transform.position);
            yield return TwoFrames();
            Assert.That(game.CollectedCount, Is.EqualTo(8));
            AssertHintHidden("Finding a star must end the old hint immediately.");
            Assert.That(HintWait, Is.GreaterThan(7.5f));
            MoveBall(ParkPosition);
            yield return TwoFrames();
            yield return WaitFor(() => HintWait <= .5f, 9, "The last star must get its own search delay.");
            AssertHintHidden("A recent pickup must prevent an immediate hint for the last star.");
            yield return WaitFor(() => HintVisible, 2, "The final star should eventually receive a hint.");

            game.SetMode(RallyMode.Versus);
            yield return TwoFrames();
            AssertHintHidden("Collection hints cannot leak into versus mode.");
            AssertFreshWait();
            game.SetMode(RallyMode.StarCollection);
            yield return TwoFrames();
            AssertHintHidden("Returning to collection must start a fresh round without hints.");
            AssertFreshWait();

            game.BallLightEnabled = false;
            yield return WaitFor(() => game.CanAdvanceBall, 3, "Collection must serve after a mode switch.");
            for (int i = 0; i < CooperativeCoinGame.CoinsPerRound; i++)
                yield return CollectOnly(i);
            Assert.That(game.RoundComplete, Is.True);
            AssertHintHidden("A completed round must never display a leftover hint.");
            yield return WaitFor(() => game.Round == 2, 4, "Completing all stars must start the next round.");
            AssertHintHidden("The next round must begin without a hint from the old layout.");
            AssertFreshWait();
            Assert.That(game.CollectedCount, Is.Zero);
        }

        IEnumerator LeaveTwoStars()
        {
            for (int i = 0; i < CooperativeCoinGame.CoinsPerRound; i++)
            {
                if (i == RemainingStars[0] || i == RemainingStars[1]) continue;
                yield return CollectOnly(i);
            }
            Assert.That(game.CollectedCount, Is.EqualTo(7));
            game.BallLightEnabled = false;
            MoveBall(ParkPosition);
            game.BallLightEnabled = true;
            yield return TwoFrames();
            AssertHintHidden("Two remaining stars must allow time to search before giving a hint.");
            AssertHintDoesNotRevealStars();
        }

        IEnumerator CollectOnly(int index)
        {
            int before = game.CollectedCount;
            // Resetting the swept segment while dark avoids collecting stars on
            // the route to the target and exercises the real pickup code.
            game.BallLightEnabled = false;
            MoveBall(Coin(index).transform.position);
            game.BallLightEnabled = true;
            yield return TwoFrames();
            Assert.That(game.CollectedCount, Is.EqualTo(before + 1), "Only the intended star should be collected.");
        }

        IEnumerator AssertFrozenHint(string reason)
        {
            yield return TwoFrames();
            float wait = HintWait;
            float age = ReadFloat("starHintAge");
            Assert.That(game.CanAdvanceBall, Is.False, reason);
            AssertHintHidden(reason + " must hide both parts of the hint.");
            yield return new WaitForSecondsRealtime(.2f);
            yield return TwoFrames();
            Assert.That(HintWait, Is.EqualTo(wait).Within(.0001f), reason + " must freeze the wait.");
            Assert.That(ReadFloat("starHintAge"), Is.EqualTo(age).Within(.0001f), reason + " must freeze the pulse.");
            AssertHintHidden(reason + " must keep hints hidden.");
        }

        void AssertHintDoesNotRevealStars()
        {
            Assert.That(game.CollectedCount, Is.EqualTo(7), "A hint must not award a star.");
            foreach (int index in RemainingStars)
            {
                Assert.That(game.CoinVisibility(index), Is.Zero);
                Assert.That(IsVisible(Coin(index)), Is.False, "The actual star still requires the ball's light.");
                Assert.That(IsVisible(Coin(index).transform.Find("Warm halo").GetComponent<SpriteRenderer>()), Is.False,
                    "The star's normal halo must remain hidden as well.");
            }
        }

        void AssertFreshWait()
        {
            Assert.That(HintWait, Is.EqualTo(8).Within(.001f));
            Assert.That(ReadFloat("starHintAge"), Is.LessThan(0));
        }

        void AssertHintHidden(string message)
        {
            Assert.That(IsVisible(Speck), Is.False, message);
            Assert.That(IsVisible(HintHalo), Is.False, message);
        }

        int ClosestRemainingStar()
        {
            float first = Vector2.Distance(Speck.transform.position, Coin(RemainingStars[0]).transform.position);
            float second = Vector2.Distance(Speck.transform.position, Coin(RemainingStars[1]).transform.position);
            return first < second ? RemainingStars[0] : RemainingStars[1];
        }

        SpriteRenderer Coin(int index) => game.transform.Find("Cooperative court/Shared coin " + (index + 1))
            .GetComponent<SpriteRenderer>();
        SpriteRenderer Speck => game.transform.Find("Cooperative court/Star hint speck").GetComponent<SpriteRenderer>();
        SpriteRenderer HintHalo => game.transform.Find("Cooperative court/Star hint halo").GetComponent<SpriteRenderer>();
        bool HintVisible => IsVisible(Speck);
        float HintWait => ReadFloat("starHintWait");

        float ReadFloat(string name)
        {
            var field = typeof(CooperativeCoinGame).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "The hint clock must be available for read-only inspection.");
            return (float)field.GetValue(game);
        }

        static bool IsVisible(SpriteRenderer renderer) => renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.color.a > .001f;

        void MoveBall(Vector2 position)
        {
            ball.Body.position = position;
            ball.transform.position = position;
            ball.Body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
        }

        static IEnumerator TwoFrames()
        {
            int frame = Time.frameCount;
            double deadline = Time.realtimeSinceStartupAsDouble + 3;
            while (Time.frameCount < frame + 2 && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(Time.frameCount, Is.GreaterThanOrEqualTo(frame + 2), "Actual player frames must advance before inspecting artwork.");
        }

        static IEnumerator WaitFor(Func<bool> condition, float timeout, string message)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + timeout;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            game = null;
            ball = null;
        }
    }
}
