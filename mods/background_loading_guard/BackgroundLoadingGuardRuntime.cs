using System.Runtime.InteropServices;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Types;
using UI.Views;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace SneakOut.BackgroundLoadingGuard;

internal static class BackgroundLoadingGuardRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static BackgroundLoadingGuardConfig? _configuration;
    private static bool _loadingFlowActive;
    private static bool _runInBackgroundForced;
    private static bool _runInBackgroundBeforeLoading;
    private static bool _watcherInstalled;
    private static UnityAction<Scene, LoadSceneMode>? _sceneLoadedAction;

    public static void Initialize(ManualLogSource logger, BackgroundLoadingGuardConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(BackgroundLoadingGuardPlugin.PluginGuid);
        _harmony.PatchAll();
        if (IsEnabled())
        {
            EnsureFocusWatcher();
            BeginLoadingFlow("Initialize");
        }
    }

    public static void HandleLoadingScreenStart(LoadingScreenView loadingScreenView)
    {
        if (!IsEnabled())
        {
            return;
        }

        BeginLoadingFlow("LoadingScreenView.Start");
        Log($"LoadingScreenView.Start: progress={loadingScreenView.LoadingProgress:0.000}");
    }

    public static void HandleLoadSceneAsync(SceneType sceneType)
    {
        if (!IsEnabled())
        {
            return;
        }

        BeginLoadingFlow("LoadingScreenView.LoadSceneAsync");
        Log($"LoadingScreenView.LoadSceneAsync: sceneType={sceneType}");
    }

    public static void HandleAllowSceneActivation(SceneType sceneType)
    {
        if (IsEnabled())
        {
            Log($"LoadingScreenView.AllowSceneActivation: sceneType={sceneType}");
        }
    }

    public static void HandleApplicationFocus(bool hasFocus)
    {
        if (!IsEnabled() || !_loadingFlowActive)
        {
            return;
        }

        EnsureRunInBackground("OnApplicationFocus");
        Log($"Loading focus changed: hasFocus={hasFocus}");
    }

    public static void HandleApplicationPause(bool pauseStatus)
    {
        if (!IsEnabled() || !_loadingFlowActive)
        {
            return;
        }

        EnsureRunInBackground("OnApplicationPause");
        Log($"Loading pause changed: pauseStatus={pauseStatus}");
    }

    private static bool IsEnabled()
    {
        return _configuration is not null && _configuration.EnableMod.Value;
    }

    private static void BeginLoadingFlow(string source)
    {
        if (!_loadingFlowActive)
        {
            _loadingFlowActive = true;
            _runInBackgroundBeforeLoading = Application.runInBackground;
        }

        EnsureRunInBackground(source);
    }

    private static void EndLoadingFlow()
    {
        if (_runInBackgroundForced)
        {
            Application.runInBackground = _runInBackgroundBeforeLoading;
            _runInBackgroundForced = false;
        }

        _loadingFlowActive = false;
    }

    private static void EnsureRunInBackground(string source)
    {
        if (_configuration is null || !_configuration.EnableMod.Value || !_configuration.ForceRunInBackground.Value)
        {
            return;
        }

        if (Application.runInBackground)
        {
            return;
        }

        Application.runInBackground = true;
        _runInBackgroundForced = true;
        Log($"runInBackground forced by {source}");
    }

    private static void EnsureFocusWatcher()
    {
        if (_watcherInstalled)
        {
            return;
        }

        ClassInjector.RegisterTypeInIl2Cpp<LoadingFocusWatcher>();
        var watcherObject = new GameObject("BackgroundLoadingGuardWatcher");
        UnityEngine.Object.DontDestroyOnLoad(watcherObject);
        watcherObject.hideFlags = HideFlags.HideAndDontSave;
        watcherObject.AddComponent<LoadingFocusWatcher>();
        _sceneLoadedAction = (UnityAction<Scene, LoadSceneMode>)HandleSceneLoaded;
        SceneManager.sceneLoaded += _sceneLoadedAction;
        _watcherInstalled = true;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!_loadingFlowActive)
        {
            return;
        }

        Log($"Scene loaded: name={scene.name}, mode={mode}");
        EndLoadingFlow();
    }

    private static void Log(string message)
    {
        if (_configuration is null || !_configuration.EnableLogging.Value)
        {
            return;
        }

        _logger?.LogInfo(message);
    }

    private sealed class LoadingFocusWatcher : MonoBehaviour
    {
        public LoadingFocusWatcher(IntPtr pointer) : base(pointer)
        {
        }

        public LoadingFocusWatcher() : base(ClassInjector.DerivedConstructorPointer<LoadingFocusWatcher>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            BackgroundLoadingGuardRuntime.HandleApplicationFocus(hasFocus);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            BackgroundLoadingGuardRuntime.HandleApplicationPause(pauseStatus);
        }
    }
}
