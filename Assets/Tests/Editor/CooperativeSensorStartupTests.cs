using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Naispo.Interaction;
using Naispo.MultiKinect;

namespace Naispo.Tests
{
    public sealed class CooperativeSensorStartupTests
    {
        CooperativeRallyApp app;
        bool sceneOpened;

        static KinectDiscovery.DeviceInfo Device(int index, string serial, string error = null) =>
            new KinectDiscovery.DeviceInfo { index = index, serial = serial, error = error };

        static KinectDiscovery.Result Inventory(params KinectDiscovery.DeviceInfo[] devices) =>
            new KinectDiscovery.Result { count = devices.Length, devices = devices };

        [Test]
        public void AutomaticSlotsUseHealthyCamerasInDeviceIndexOrder()
        {
            var inventory = Inventory(Device(9, "RIGHT"), Device(1, "BUSY", "already in use"),
                Device(4, "LEFT"), Device(12, "SPARE"));
            Assert.That(KinectDiscovery.TrySelect(inventory, new[] { "", "" }, out var selected, out var error), Is.True, error);
            Assert.That(selected.Length, Is.EqualTo(2));
            Assert.That(selected[0].serial, Is.EqualTo("LEFT"));
            Assert.That(selected[1].serial, Is.EqualTo("RIGHT"));
        }

        [Test]
        public void ExplicitSecondSlotIsReservedBeforeAutomaticFirstSlot()
        {
            var inventory = Inventory(Device(0, "PINNED"), Device(1, "OTHER"));
            Assert.That(KinectDiscovery.TrySelect(inventory, new[] { "", "PINNED" }, out var selected, out var error), Is.True, error);
            Assert.That(selected[0].serial, Is.EqualTo("OTHER"));
            Assert.That(selected[1].serial, Is.EqualTo("PINNED"));
        }

        [Test]
        public void ExplicitSerialsPreserveRequestedPlayerSides()
        {
            var inventory = Inventory(Device(0, "A"), Device(1, "B"));
            Assert.That(KinectDiscovery.TrySelect(inventory, new[] { "B", "A" }, out var selected, out var error), Is.True, error);
            Assert.That(selected[0].index, Is.EqualTo(1));
            Assert.That(selected[1].index, Is.Zero);
        }

        [Test]
        public void MissingExplicitSerialDoesNotSilentlyChooseAnotherCamera()
        {
            AssertRejected(Inventory(Device(0, "A"), Device(1, "B")), new[] { "OLD-PC-CAMERA", "" });
        }

        [Test]
        public void DuplicateExplicitSerialCannotFeedBothPlayers()
        {
            AssertRejected(Inventory(Device(0, "A"), Device(1, "B")), new[] { "A", "A" });
        }

        [Test]
        public void ExplicitCameraWithOpenErrorIsRejectedEvenIfTwoOtherCamerasAreHealthy()
        {
            AssertRejected(Inventory(Device(0, "A", "access denied"), Device(1, "B"), Device(2, "C")),
                new[] { "A", "" });
        }

        [Test]
        public void TwoEnumeratedDevicesAreInsufficientWhenOneCannotOpen()
        {
            AssertRejected(Inventory(Device(0, "A"), Device(1, "B", "cannot open device")), new[] { "", "" });
        }

        [Test]
        public void DisconnectedOrMalformedInventoryIsRejected()
        {
            AssertRejected(Inventory(), new[] { "", "" });
            AssertRejected(null, new[] { "", "" });
            AssertRejected(new KinectDiscovery.Result { count = 2, devices = null }, new[] { "", "" });
        }

        static void AssertRejected(KinectDiscovery.Result inventory, string[] requested)
        {
            Assert.That(KinectDiscovery.TrySelect(inventory, requested, out _, out var error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty, "Failure must explain why sensor startup cannot proceed.");
        }

        [UnityTest]
        public IEnumerator MissingWorkerReportsStartupFailureInsteadOfAskingForHands()
        {
            const string scenePath = "Assets/Scenes/CooperativeCoinRally.unity";
            Assert.That(File.Exists(scenePath), Is.True);
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            sceneOpened = true;
            yield return new EnterPlayMode();
            app = UnityEngine.Object.FindFirstObjectByType<CooperativeRallyApp>();
            Assert.That(app, Is.Not.Null);
            app.Sensors.settings.workerExecutable = Path.Combine(Application.temporaryCachePath,
                "MissingKinectWorker_" + Guid.NewGuid().ToString("N") + ".exe");
            app.UseSensors();
            // UseSensors schedules launch for Update. Force automatic selection
            // before that update so saved serial preferences cannot affect this test.
            app.Sensors.settings.serialNumbers = new[] { "", "" };
            double deadline = Time.realtimeSinceStartupAsDouble + 3;
            while (app.Sensors.StartupState != KinectStartupState.Error && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.That(app.Sensors.StartupState, Is.EqualTo(KinectStartupState.Error));
            StringAssert.Contains("KinectWorker", app.Sensors.LifecycleStatus);
            for (int slot = 0; slot < 2; slot++)
            {
                StringAssert.Contains("KinectWorker", app.PlayerStatus(slot), "Show the startup cause in each player's status.");
                StringAssert.DoesNotContain("両手", app.PlayerStatus(slot));
                Assert.That(app.Sensors.WorkerRunning(slot), Is.False);
            }
            Assert.That(app.Game.InputReady, Is.False);
            Assert.That(app.BothPlayersReady, Is.False);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (!sceneOpened) yield break;
            if (Application.isPlaying)
            {
                if (app != null && app.Sensors != null)
                {
                    app.Sensors.StopWorkers();
                    app.Sensors.enabled = false;
                    double deadline = Time.realtimeSinceStartupAsDouble + 5;
                    while (app.Sensors.IsReconfiguring && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                }
                yield return new ExitPlayMode();
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            app = null;
            sceneOpened = false;
        }
    }
}
