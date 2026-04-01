using BepInEx.Logging;
using Events;
using HarmonyLib;
using Networking.PGOS;
using System;
using System.Reflection;
using System.Text.RegularExpressions;
using UI.Views.Lobby.People;

namespace SneakOut.DoublePartyInviteFix;

internal static class DoublePartyInviteFixRuntime
{
    private static readonly Regex QueryRegex = new("(?:(?:^|[?&;,| ])(?<key>lobbyId|lobby|room|region|regionToken)=(?<value>[^?&;,| ]+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Type? ObscuredPrefsType = AccessTools.TypeByName("CodeStage.AntiCheat.Storage.ObscuredPrefs");
    private static readonly MethodInfo? ObscuredPrefsGetStringMethod = ObscuredPrefsType is null
        ? null
        : AccessTools.Method(ObscuredPrefsType, "GetString", new[] { typeof(string), typeof(string) });
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static DoublePartyInviteFixConfig? _configuration;

    public static void Initialize(ManualLogSource logger, DoublePartyInviteFixConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(DoublePartyInviteFixPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static bool TryHandleJoinLobbyEvent(PgosLobby pgosLobby, Il2CppSystem.EventArgs? args)
    {
        if (_configuration is null || !_configuration.EnableMod.Value)
        {
            return false;
        }

        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"TryHandleJoinLobbyEvent: argsNull={args is null}, pointer={(args is null ? IntPtr.Zero : args.Pointer)}");
        }

        if (args is null || args.Pointer == IntPtr.Zero)
        {
            return false;
        }

        var joinLobbyEvent = new JoinLobbyEvent(args.Pointer);
        var lobbyId = joinLobbyEvent.LobbyId;
        var region = joinLobbyEvent.Region;
        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"JoinLobbyEvent payload: lobbyId='{lobbyId}', region='{region}'");
        }

        if (string.IsNullOrEmpty(lobbyId) || string.IsNullOrEmpty(region))
        {
            return false;
        }

        region = NormalizeRegion(region);
        if (string.IsNullOrEmpty(region))
        {
            return false;
        }

        pgosLobby.SetRegion(region);
        pgosLobby._spookedLobbyUtils.JoinLobby(lobbyId, region);

        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"JoinLobbyEvent override: lobbyId={lobbyId}, region={region}");
        }

        return true;
    }

    public static bool TryHandleTeamInvitationClicked(PgosLobby pgosLobby, Il2CppSystem.EventArgs? args)
    {
        if (_configuration is null || !_configuration.EnableMod.Value)
        {
            return false;
        }

        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"TryHandleTeamInvitationClicked: argsNull={args is null}, pointer={(args is null ? IntPtr.Zero : args.Pointer)}");
        }

        if (args is null || args.Pointer == IntPtr.Zero)
        {
            return false;
        }

        var invitationClickedEvent = new OnTeamInvitationClickedEvent(args.Pointer);
        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo(
                $"TeamInvitationClicked payload: pgosId={invitationClickedEvent.PgosId}, steamId={invitationClickedEvent.SteamId}, invitationsCount={pgosLobby.PlayfabIntvitationsData.Count}");
        }

        if (!TryGetInvitationData(pgosLobby, invitationClickedEvent.PgosId, out var invitationData))
        {
            if (_configuration.EnableLogging.Value)
            {
                _logger?.LogInfo(
                    $"TeamInvitationClicked no cached invitation: pgosId={invitationClickedEvent.PgosId}, invitationsCount={pgosLobby.PlayfabIntvitationsData.Count}");
            }

            return false;
        }

        if (!TryParseJoinTarget(invitationData.ConnectionString, out var lobbyId, out var region))
        {
            if (_configuration.EnableLogging.Value)
            {
                _logger?.LogInfo(
                    $"TeamInvitationClicked parse miss: pgosId={invitationClickedEvent.PgosId}, connectionString='{invitationData.ConnectionString}'");
            }

            return false;
        }

        region = NormalizeRegion(region);
        if (string.IsNullOrEmpty(region))
        {
            if (_configuration.EnableLogging.Value)
            {
                _logger?.LogInfo($"TeamInvitationClicked invalid normalized region: rawConnectionString='{invitationData.ConnectionString}'");
            }

            return false;
        }

        pgosLobby.SetRegion(region);
        pgosLobby._spookedLobbyUtils.JoinLobby(lobbyId, region);

        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo(
                $"TeamInvitationClicked override: pgosId={invitationClickedEvent.PgosId}, lobbyId={lobbyId}, region={region}");
        }

        return true;
    }

    public static void LogInvitationAccept(InvitationRecord invitationRecord)
    {
        if (_configuration is null || !_configuration.EnableLogging.Value)
        {
            return;
        }

        var invitationData = invitationRecord.InvitationData;
        _logger?.LogInfo(
            $"Invitation accept: pgosId={invitationData.PgosId}, username={invitationData.Username}, connectionString='{invitationData.ConnectionString}'");
    }

    public static void LogJoinLobbyFromInvitation(PgosLobby pgosLobby)
    {
        if (_configuration is null || !_configuration.EnableLogging.Value)
        {
            return;
        }

        if (pgosLobby.PlayfabIntvitationsData.Count == 0)
        {
            _logger?.LogInfo("JoinLobbyFromInvitation: invitationsCount=0");
            return;
        }

        var cachedInvitation = pgosLobby.PlayfabIntvitationsData[0];
        _logger?.LogInfo(
            $"JoinLobbyFromInvitation: invitationsCount={pgosLobby.PlayfabIntvitationsData.Count}, firstPgosId={cachedInvitation.PgosId}, firstUsername={cachedInvitation.Username}, firstConnectionString='{cachedInvitation.ConnectionString}'");
    }

    public static bool TryHandleJoinLobbyFromInvitation(PgosLobby pgosLobby)
    {
        if (_configuration is null || !_configuration.EnableMod.Value || pgosLobby.PlayfabIntvitationsData.Count == 0)
        {
            return false;
        }

        var invitationData = pgosLobby.PlayfabIntvitationsData[0];
        if (!TryParseJoinTarget(invitationData.ConnectionString, out var lobbyId, out var region))
        {
            if (_configuration.EnableLogging.Value)
            {
                _logger?.LogInfo($"JoinLobbyFromInvitation parse miss: connectionString='{invitationData.ConnectionString}'");
            }

            return false;
        }

        region = NormalizeRegion(region);
        if (string.IsNullOrEmpty(region))
        {
            if (_configuration.EnableLogging.Value)
            {
                _logger?.LogInfo($"JoinLobbyFromInvitation invalid normalized region: rawConnectionString='{invitationData.ConnectionString}'");
            }

            return false;
        }

        pgosLobby.SetRegion(region);
        pgosLobby._spookedLobbyUtils.JoinLobby(lobbyId, region);

        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"JoinLobbyFromInvitation override: lobbyId={lobbyId}, region={region}");
        }

        return true;
    }

    private static bool TryGetInvitationData(PgosLobby pgosLobby, string pgosId, out PlayfabInvitationData invitationData)
    {
        for (var index = 0; index < pgosLobby.PlayfabIntvitationsData.Count; index++)
        {
            var candidate = pgosLobby.PlayfabIntvitationsData[index];
            if (string.Equals(candidate.PgosId, pgosId, StringComparison.Ordinal))
            {
                invitationData = candidate;
                return true;
            }
        }

        if (pgosLobby.PlayfabIntvitationsData.Count == 1)
        {
            invitationData = pgosLobby.PlayfabIntvitationsData[0];
            return true;
        }

        invitationData = new PlayfabInvitationData(string.Empty, string.Empty, string.Empty);
        return false;
    }

    private static bool TryParseJoinTarget(string connectionString, out string lobbyId, out string region)
    {
        lobbyId = string.Empty;
        region = string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (_configuration is not null && _configuration.EnableLogging.Value)
            {
                _logger?.LogInfo("TryParseJoinTarget: empty connection string");
            }
            return false;
        }

        foreach (Match match in QueryRegex.Matches(connectionString))
        {
            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value;
            if (string.Equals(key, "region", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "regionToken", StringComparison.OrdinalIgnoreCase))
            {
                region = value;
            }
            else
            {
                lobbyId = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(lobbyId) && !string.IsNullOrWhiteSpace(region))
        {
            if (_configuration is not null && _configuration.EnableLogging.Value)
            {
                _logger?.LogInfo($"TryParseJoinTarget: matched query format -> lobbyId='{lobbyId}', region='{region}'");
            }
            return true;
        }

        var lastDotIndex = connectionString.LastIndexOf('.');
        if (lastDotIndex > 0 && lastDotIndex < connectionString.Length - 1)
        {
            var candidateLobbyId = connectionString[..lastDotIndex];
            var candidateRegion = connectionString[(lastDotIndex + 1)..];
            if (!string.IsNullOrWhiteSpace(candidateLobbyId) && !string.IsNullOrWhiteSpace(candidateRegion))
            {
                lobbyId = candidateLobbyId;
                region = candidateRegion;
                if (_configuration is not null && _configuration.EnableLogging.Value)
                {
                    _logger?.LogInfo($"TryParseJoinTarget: matched dotted format -> lobbyId='{lobbyId}', region='{region}'");
                }
                return true;
            }
        }

        var tokens = connectionString
            .Split(new[] { '|', ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(lobbyId) && token.All(char.IsDigit))
            {
                lobbyId = token;
                continue;
            }

            if (string.IsNullOrWhiteSpace(region) && token.Any(char.IsLetter) && token.All(static character => char.IsLetterOrDigit(character) || character is '_' or '-'))
            {
                region = token;
            }
        }

        if (_configuration is not null && _configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"TryParseJoinTarget: fallback result -> lobbyId='{lobbyId}', region='{region}', raw='{connectionString}'");
        }

        return !string.IsNullOrWhiteSpace(lobbyId) && !string.IsNullOrWhiteSpace(region);
    }

    private static string NormalizeRegion(string region)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return string.Empty;
        }

        if (!region.StartsWith("nakama-", StringComparison.OrdinalIgnoreCase))
        {
            return region;
        }

        var defaultRegion = TryGetStoredDefaultRegion();
        if (_configuration is not null && _configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"NormalizeRegion: raw='{region}', defaultRegion='{defaultRegion}'");
        }

        return string.IsNullOrWhiteSpace(defaultRegion) ? string.Empty : defaultRegion;
    }

    private static string TryGetStoredDefaultRegion()
    {
        if (ObscuredPrefsGetStringMethod is null)
        {
            return string.Empty;
        }

        try
        {
            return ObscuredPrefsGetStringMethod.Invoke(null, new object[] { "DEFAULT_REGION", string.Empty }) as string ?? string.Empty;
        }
        catch (Exception exception)
        {
            if (_configuration is not null && _configuration.EnableLogging.Value)
            {
                _logger?.LogInfo($"TryGetStoredDefaultRegion failed: {exception.GetType().Name}: {exception.Message}");
            }

            return string.Empty;
        }
    }
}
