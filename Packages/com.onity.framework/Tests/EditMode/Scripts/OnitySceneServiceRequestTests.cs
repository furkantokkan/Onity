using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.SceneFlow;
using UnityEngine.SceneManagement;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Proves the value rules of the scene service without loading scenes: request defaults and copies, how a
    /// requested name or path resolves against the Build Settings, state equality, and the requests the service
    /// rejects before it starts anything.
    /// </summary>
    [TestFixture]
    public sealed class OnitySceneServiceRequestTests
    {
        private static readonly string[] s_buildScenes =
        {
            "Assets/Scenes/Boot.unity",
            "Assets/Scenes/Menu.unity",
            "Assets/Levels/Forest/Menu.unity",
            "Assets/Levels/Forest/Level1.unity"
        };

        [Test]
        public void Single_DefaultsToTheDefaultCoverAndActivates()
        {
            Data data = new Data();
            OnitySceneRequest request = OnitySceneRequest.Single("Game", data);

            Assert.That(request.Scene, Is.EqualTo("Game"));
            Assert.That(request.Mode, Is.EqualTo(LoadSceneMode.Single));
            Assert.That(request.EnterData, Is.SameAs(data));
            Assert.That(request.CoverMode, Is.EqualTo(OnitySceneCoverMode.Default));
            Assert.That(request.Cover, Is.Null);
            Assert.That(request.SetActive, Is.True);
            Assert.That(request.PreferPreparedScene, Is.False);
        }

        [Test]
        public void Additive_HasNoCoverAndActivatesOnlyWhenAsked()
        {
            OnitySceneRequest stays = OnitySceneRequest.Additive("Hud");
            OnitySceneRequest activates = OnitySceneRequest.Additive("Level", setActive: true);

            Assert.That(stays.Mode, Is.EqualTo(LoadSceneMode.Additive));
            Assert.That(stays.CoverMode, Is.EqualTo(OnitySceneCoverMode.None));
            Assert.That(stays.SetActive, Is.False);
            Assert.That(activates.SetActive, Is.True);
        }

        [Test]
        public void WithMethods_ReturnChangedCopies()
        {
            FakeCover iris = new FakeCover();
            OnitySceneRequest original = OnitySceneRequest.Additive("Level", setActive: true);

            OnitySceneRequest custom = original.WithCover(iris);
            OnitySceneRequest none = custom.WithoutCover();
            OnitySceneRequest withDefault = none.WithDefaultCover();
            OnitySceneRequest prepared = OnitySceneRequest.Single("Game").WithCover(iris).WithPreparedScene();

            Assert.That(original.CoverMode, Is.EqualTo(OnitySceneCoverMode.None), "the original is unchanged");
            Assert.That(custom.CoverMode, Is.EqualTo(OnitySceneCoverMode.Custom));
            Assert.That(custom.Cover, Is.SameAs(iris));
            Assert.That(custom.SetActive, Is.True, "a copy keeps the activation choice");
            Assert.That(none.CoverMode, Is.EqualTo(OnitySceneCoverMode.None));
            Assert.That(none.Cover, Is.Null);
            Assert.That(withDefault.CoverMode, Is.EqualTo(OnitySceneCoverMode.Default));
            Assert.That(prepared.PreferPreparedScene, Is.True);
            Assert.That(prepared.Cover, Is.SameAs(iris), "the prepared preference keeps the cover");
            Assert.Throws<ArgumentNullException>(() => original.WithCover(null));
        }

        [TestCase("Boot", "Assets/Scenes/Boot.unity")]
        [TestCase("boot", "Assets/Scenes/Boot.unity")]
        [TestCase("Level1", "Assets/Levels/Forest/Level1.unity")]
        [TestCase("Assets/Scenes/Menu.unity", "Assets/Scenes/Menu.unity")]
        [TestCase("Assets/Scenes/Menu", "Assets/Scenes/Menu.unity")]
        [TestCase("Forest/Menu", "Assets/Levels/Forest/Menu.unity")]
        public void Resolve_MatchesExactlyOneBuildScene(string requested, string expected)
        {
            bool resolved = OnitySceneManagerBackend.TryResolveBuildScene(
                requested,
                s_buildScenes,
                out string key,
                out string error);

            Assert.That(resolved, Is.True, error);
            Assert.That(key, Is.EqualTo(expected));
        }

        [Test]
        public void Resolve_SharedNameNeedsAPath()
        {
            bool resolved = OnitySceneManagerBackend.TryResolveBuildScene("Menu", s_buildScenes, out string key, out string error);

            Assert.That(resolved, Is.False);
            Assert.That(key, Is.Null);
            Assert.That(error, Does.Contain("matches 2 scenes"));
        }

        [TestCase("Missing")]
        [TestCase("enu")]
        [TestCase("Scenes/Men")]
        [TestCase("")]
        [TestCase(null)]
        public void Resolve_NoMatchIsAnError(string requested)
        {
            bool resolved = OnitySceneManagerBackend.TryResolveBuildScene(requested, s_buildScenes, out string key, out string error);

            Assert.That(resolved, Is.False);
            Assert.That(key, Is.Null);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void State_ComparesByValue()
        {
            OnitySceneOperationState loading = new OnitySceneOperationState(3, OnitySceneOperationPhase.Loading, "Game", 0.5f);

            Assert.That(loading, Is.EqualTo(new OnitySceneOperationState(3, OnitySceneOperationPhase.Loading, "Game", 0.5f)));
            Assert.That(loading == new OnitySceneOperationState(3, OnitySceneOperationPhase.Loading, "Game", 0.6f), Is.False);
            Assert.That(loading != new OnitySceneOperationState(4, OnitySceneOperationPhase.Loading, "Game", 0.5f), Is.True);
            Assert.That(OnitySceneOperationState.Idle, Is.EqualTo(default(OnitySceneOperationState)));
            Assert.That(OnitySceneOperationState.Idle.Phase, Is.EqualTo(OnitySceneOperationPhase.Idle));
        }

        [Test]
        public void Result_ReportsSuccessOnlyForSucceeded()
        {
            OnitySceneResult success = new OnitySceneResult(OnitySceneResultStatus.Succeeded, 1, default);
            OnitySceneResult timedOut = new OnitySceneResult(OnitySceneResultStatus.ReadinessTimedOut, 2, default, "late");

            Assert.That(success.IsSuccess, Is.True);
            Assert.That(timedOut.IsSuccess, Is.False);
            Assert.That(timedOut.ToString(), Does.Contain("late"));
        }

        [Test]
        public void Service_RejectsWhatItCannotStart()
        {
            using OnitySceneService service = new OnitySceneService();

            Task<OnitySceneResult> cancelled = service
                .LoadAsync(OnitySceneRequest.Single("Game"), new CancellationToken(true))
                .AsTask();
            OnitySceneResult missing = service
                .LoadAsync(OnitySceneRequest.Single("Onity-Missing-Scene"))
                .AsTask()
                .Result;
            OnitySceneResult additivePrepared = service
                .LoadAsync(OnitySceneRequest.Additive("Onity-Missing-Scene").WithPreparedScene())
                .AsTask()
                .Result;
            OnitySceneResult notLoaded = service.UnloadAsync(default).AsTask().Result;

            Assert.That(cancelled.IsCanceled, Is.True, "a cancelled caller starts nothing");
            Assert.That(missing.Status, Is.EqualTo(OnitySceneResultStatus.InvalidTarget));
            Assert.That(missing.OperationId, Is.EqualTo(0));
            Assert.That(missing.Message, Does.Contain("Build Settings"));
            Assert.That(additivePrepared.Status, Is.EqualTo(OnitySceneResultStatus.InvalidTarget));
            Assert.That(notLoaded.Status, Is.EqualTo(OnitySceneResultStatus.NotLoaded));
            Assert.That(service.IsBusy, Is.False);
            Assert.That(service.State.Value, Is.EqualTo(OnitySceneOperationState.Idle));

            service.Dispose();
            Assert.That(service.LoadAsync(OnitySceneRequest.Single("Game")).AsTask().Result.Status,
                Is.EqualTo(OnitySceneResultStatus.Disposed));
            Assert.DoesNotThrow(service.Dispose);
        }

        private sealed class Data : IOnitySceneEnterData
        {
        }

        private sealed class FakeCover : IOnitySceneCover
        {
            public bool IsVisible => false;

            public Task ShowAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task HideAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }
    }
}
