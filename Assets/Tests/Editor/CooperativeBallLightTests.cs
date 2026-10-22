using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Naispo.Interaction;

namespace Naispo.Tests
{
    public sealed class CooperativeBallLightTests
    {
        CooperativeCoinGame game;
        AirHockeyBall ball;

        [UnitySetUp]
        public IEnumerator CreateCourt()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            // Local physics keeps the test's ball positions stable while real
            // Update and LateUpdate drive the game, lighting, and round timers.
            var scene = SceneManager.CreateScene("Cooperative light test",
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
        }

        [UnityTest]
        public IEnumerator OnlyLitCoinsAndTheirHalosAreVisibleWithoutAnArcGuide()
        {
            Assert.That(game.BallLightEnabled, Is.True);
            Assert.That(game.BallLightRadius, Is.EqualTo(1.8f).Within(.001f));
            AssertHidden(4, "The center coin is outside the starting serve's light.");
            AssertBallLight(true);
            foreach (var line in game.GetComponentsInChildren<LineRenderer>(true))
                Assert.That(line.enabled && line.gameObject.activeInHierarchy, Is.False,
                    "A persistent arc would reveal hidden coin positions.");

            game.SetPaused(true);
            var coin = Coin(4);
            MoveBall((Vector2)coin.transform.position + Vector2.up * game.BallLightRadius * .15f);
            yield return TwoFrames();
            Assert.That(game.CoinVisibility(4), Is.EqualTo(1).Within(.001f));
            Assert.That(IsVisible(coin), Is.True);
            Assert.That(IsVisible(Halo(4)), Is.True);
            AssertBallLight(true);

            MoveBall((Vector2)coin.transform.position + Vector2.up * game.BallLightRadius * .85f);
            yield return TwoFrames();
            Assert.That(game.CoinVisibility(4), Is.GreaterThan(0).And.LessThan(1));
            Assert.That(coin.color.a, Is.GreaterThan(0).And.LessThan(1));

            MoveBall((Vector2)coin.transform.position + Vector2.up * (game.BallLightRadius + .2f));
            yield return TwoFrames();
            AssertHidden(4, "Moving the light away must hide both the coin and its halo.");
        }

        [UnityTest]
        public IEnumerator LightOffBlocksStationaryAndSweptPickupsButLightOnCollectsFastCrossing()
        {
            game.BallLightEnabled = false;
            AssertBallLight(false);
            yield return WaitFor(() => !game.WaitingToServe, 3, "The initial serve must start.");
            // The edge coin is the only one at this height on this segment.
            Vector2 target = Coin(0).transform.position;
            MoveBall(target);
            yield return TwoFrames();
            AssertHidden(0, "A ball touching a coin with its light off must not reveal it.");
            Assert.That(game.TeamScore, Is.Zero);

            Vector2 left = target + Vector2.left * 3;
            Vector2 right = target + Vector2.right * 3;
            MoveBall(left);
            yield return TwoFrames();
            MoveBall(right);
            yield return TwoFrames();
            Assert.That(game.TeamScore, Is.Zero, "A swept contact with the light off must not collect coins.");

            MoveBall(left);
            yield return TwoFrames();
            game.BallLightEnabled = true;
            AssertBallLight(true);
            yield return TwoFrames();
            Assert.That(game.TeamScore, Is.Zero);
            AssertHidden(0, "The target starts outside the light radius.");
            Assert.That(Vector2.Distance(right, target), Is.GreaterThan(game.BallLightRadius));
            MoveBall(right);
            yield return TwoFrames();
            Assert.That(game.TeamScore, Is.EqualTo(1),
                "Light travels with a fast ball: the coin crossed between frames must still count.");
            AssertBallLight(true);
        }

        [UnityTest]
        public IEnumerator RestartImmediatelyHidesCoinsLitAtTheOldBallPosition()
        {
            game.SetPaused(true);
            MoveBall(Coin(4).transform.position);
            yield return TwoFrames();
            Assert.That(IsVisible(Coin(4)), Is.True);
            game.RestartGame();
            AssertHidden(4, "Restart must render from the new serve position without a one-frame flash.");
            yield return TwoFrames();
            AssertHidden(4, "A restarted round must keep distant coins hidden.");
            Assert.That(game.TeamScore, Is.Zero);
        }

        [UnityTest]
        public IEnumerator NextRoundRestoresOnlyCoinsInsideTheNewServeLight()
        {
            yield return WaitFor(() => !game.WaitingToServe, 3, "The initial serve must start.");
            for (int i = 0; i < CooperativeCoinGame.CoinsPerRound; i++)
            {
                MoveBall(Coin(i).transform.position);
                yield return TwoFrames();
            }
            Assert.That(game.RoundComplete, Is.True);
            Assert.That(game.TeamScore, Is.EqualTo(CooperativeCoinGame.CoinsPerRound));
            yield return WaitFor(() => game.Round == 2, 4, "A completed arc must advance to the next round.");
            Assert.That(game.CollectedCount, Is.Zero);
            AssertHidden(4, "The new arc must not expose distant coins after the completion animation.");
            Assert.That(IsVisible(Coin(CooperativeCoinGame.CoinsPerRound - 1)), Is.True,
                "The alternating right serve must reveal its nearby coin again.");
        }

        SpriteRenderer Coin(int index) => game.transform.Find("Cooperative court/Shared coin " + (index + 1))
            .GetComponent<SpriteRenderer>();

        SpriteRenderer Halo(int index) => Coin(index).transform.Find("Warm halo").GetComponent<SpriteRenderer>();

        static bool IsVisible(SpriteRenderer renderer) => renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.color.a > .001f;

        void AssertBallLight(bool visible)
        {
            foreach (string name in new[] { "Ball light", "Ball corona" })
            {
                var renderer = game.transform.Find("Cooperative court/" + name).GetComponent<SpriteRenderer>();
                Assert.That(renderer.sprite, Is.Not.Null);
                Assert.That(IsVisible(renderer), Is.EqualTo(visible), name + " must follow the light switch.");
                Assert.That(Vector3.Distance(renderer.transform.position, ball.transform.position), Is.LessThan(.001f),
                    name + " must follow the displayed ball position.");
            }
        }

        void AssertHidden(int index, string message)
        {
            Assert.That(game.CoinVisibility(index), Is.Zero, message);
            Assert.That(IsVisible(Coin(index)), Is.False, message);
            Assert.That(IsVisible(Halo(index)), Is.False, message);
        }

        void MoveBall(Vector2 position)
        {
            ball.Body.position = position;
            ball.transform.position = position;
            ball.Body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
        }

        static IEnumerator TwoFrames()
        {
            yield return null;
            yield return null;
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
