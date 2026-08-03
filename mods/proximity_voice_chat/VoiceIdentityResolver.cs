using Gameplay.Player.Components;
using HarmonyLib;
using Steamworks;

namespace SneakOut.ProximityVoiceChat;

internal static class VoiceIdentityResolver
{
    private static readonly string[] IdentityMemberNames =
    {
        "SteamId",
        "SteamID",
        "SteamID64",
        "PlatformId",
        "PlatformID",
    };

    public static bool TryResolveSteamId(SpookedNetworkPlayer player, out ulong steamId)
    {
        steamId = 0;
        try
        {
            // 1.1.10 replicates the platform identity directly on the network player. Prefer that
            // typed authoritative value; the nested registry walk remains only as a compatibility
            // fallback for the short initialization window where the network property is still 0.
            steamId = player.SteamId;
            if (IsPlausibleSteamId(steamId))
            {
                return true;
            }

            var registry = player._spookedNetworkPlayerDataRegistry;
            if (registry?._dict is null || !registry._dict.ContainsKey(player.KinguinverseId))
            {
                return false;
            }

            var playerData = registry._dict[player.KinguinverseId];
            return TryExtractSteamId(playerData, 0, new HashSet<IntPtr>(), out steamId);
        }
        catch
        {
            // Identity lookup is an optional discovery accelerator. Friends' rich presence and
            // explicitly configured peers remain available if a game update changes this shape.
            return false;
        }
    }

    private static bool TryExtractSteamId(
        object? value,
        int depth,
        HashSet<IntPtr> visited,
        out ulong steamId)
    {
        steamId = 0;
        if (value is null || depth > 2)
        {
            return false;
        }
        if (TryConvertIdentity(value, out steamId))
        {
            return IsPlausibleSteamId(steamId);
        }

        var pointerProperty = AccessTools.Property(value.GetType(), "Pointer");
        if (pointerProperty?.GetValue(value) is IntPtr pointer
            && pointer != IntPtr.Zero
            && !visited.Add(pointer))
        {
            return false;
        }

        foreach (var memberName in IdentityMemberNames)
        {
            if (TryReadMember(value, memberName, out var identity)
                && TryConvertIdentity(identity, out steamId)
                && IsPlausibleSteamId(steamId))
            {
                return true;
            }
        }

        foreach (var memberName in new[] { "BaseData", "PlayerData", "Data", "Profile", "User" })
        {
            if (TryReadMember(value, memberName, out var nested)
                && TryExtractSteamId(nested, depth + 1, visited, out steamId))
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryReadMember(object owner, string name, out object? value)
    {
        value = null;
        try
        {
            var property = AccessTools.Property(owner.GetType(), name);
            if (property is not null && property.GetIndexParameters().Length == 0)
            {
                value = property.GetValue(owner);
                return true;
            }
            var field = AccessTools.Field(owner.GetType(), name);
            if (field is not null)
            {
                value = field.GetValue(owner);
                return true;
            }
        }
        catch
        {
            // A single unavailable IL2CPP getter must not abort the conservative identity search.
        }
        return false;
    }

    private static bool TryConvertIdentity(object? value, out ulong steamId)
    {
        steamId = 0;
        switch (value)
        {
            case ulong direct:
                steamId = direct;
                return true;
            case long signed when signed > 0:
                steamId = (ulong)signed;
                return true;
            case CSteamID steam:
                steamId = steam.m_SteamID;
                return true;
            case string text:
                return ulong.TryParse(text, out steamId);
        }

        if (value is null)
        {
            return false;
        }
        try
        {
            var hasValue = AccessTools.Property(value.GetType(), "HasValue")?.GetValue(value);
            if (hasValue is bool hasUnderlyingValue && !hasUnderlyingValue)
            {
                return false;
            }
            var getValueOrDefault = AccessTools.Method(value.GetType(), "GetValueOrDefault", Type.EmptyTypes);
            if (getValueOrDefault is not null)
            {
                return TryConvertIdentity(getValueOrDefault.Invoke(value, Array.Empty<object>()), out steamId);
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    private static bool IsPlausibleSteamId(ulong value)
    {
        return value >= 76561197960265728UL && value <= 76561210000000000UL;
    }
}
