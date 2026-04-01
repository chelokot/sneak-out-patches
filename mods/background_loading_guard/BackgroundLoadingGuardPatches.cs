using HarmonyLib;
using Types;
using UI.Views;

namespace SneakOut.BackgroundLoadingGuard;

[HarmonyPatch(typeof(LoadingScreenView), "Start")]
internal static class LoadingScreenViewStartPatch
{
    private static void Postfix(LoadingScreenView __instance)
    {
        BackgroundLoadingGuardRuntime.HandleLoadingScreenStart(__instance);
    }
}

[HarmonyPatch(typeof(LoadingScreenView), "LoadSceneAsync")]
internal static class LoadingScreenViewLoadSceneAsyncPatch
{
    private static void Prefix(SceneType sceneType)
    {
        BackgroundLoadingGuardRuntime.HandleLoadSceneAsync(sceneType);
    }
}

[HarmonyPatch(typeof(LoadingScreenView), "AllowSceneActivation")]
internal static class LoadingScreenViewAllowSceneActivationPatch
{
    private static void Prefix(SceneType sceneType)
    {
        BackgroundLoadingGuardRuntime.HandleAllowSceneActivation(sceneType);
    }
}
