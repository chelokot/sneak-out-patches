using BepInEx.Logging;
using Gameplay.Interactions;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace SneakOut.CommunityDiscord;

internal static class CommunityDiscordRuntime
{
    private const string StockStatueName = "DiscordStatue_a_prefab";

    private static ManualLogSource? _logger;
    private static CommunityDiscordConfig? _configuration;
    private static Harmony? _harmony;

    public static void Initialize(ManualLogSource logger, CommunityDiscordConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(CommunityDiscordPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void ReplaceStockDiscordStatueUrl(Interactable interactable)
    {
        var stockStatue = interactable?.TryCast<SocialsStatue>();
        if (!IsEnabled()
            || stockStatue is null
            || !IsStockDiscordStatue(stockStatue))
        {
            return;
        }

        try
        {
            var inviteUrl = _configuration!.InviteUrl.Value.Trim();
            if (string.Equals(stockStatue._redirectURL, inviteUrl, StringComparison.Ordinal))
            {
                return;
            }

            stockStatue._redirectURL = inviteUrl;
            if (_configuration.EnableLogging.Value)
            {
                _logger?.LogInfo("Replaced the existing Discord statue invite URL");
            }
        }
        catch (Exception exception)
        {
            _logger?.LogWarning($"Could not replace the existing Discord statue invite URL: {exception.Message}");
        }
    }

    private static bool IsStockDiscordStatue(SocialsStatue statue)
    {
        return statue.Pointer != IntPtr.Zero
            && statue.gameObject is { } gameObject
            && string.Equals(gameObject.name, StockStatueName, StringComparison.Ordinal)
            && gameObject.scene.IsValid()
            && string.Equals(gameObject.scene.name, "Lobby", StringComparison.Ordinal);
    }

    private static bool IsEnabled()
    {
        return _configuration?.EnableMod.Value == true
            && !string.IsNullOrWhiteSpace(_configuration.InviteUrl.Value);
    }
}
