using System.Text.Json;
using Base;
using BepInEx;
using Kinguinverse.WebServiceProvider.Types_v2;
using SimplifiedSkillsRuntime = Types.Structs.SimplifiedWebPlayerSkills;
using PlayerSkillRuntime = Types.Structs.PlayerSkill;

namespace SneakOut.MummyUnlock;

internal static class MummyPerkStore
{
    private const int CurrentSchemaVersion = 1;
    private const string DefaultProfileKey = "user:unknown";
    private const string LegacyMummyCharacterKey = "runtime:12";

    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly string StoragePath = Path.Combine(
        Paths.ConfigPath,
        "chelokot.sneakout.mummy-unlock.json");
    private static readonly string LegacyUnlockEverythingStoragePath = Path.Combine(
        Paths.ConfigPath,
        "chelokot.sneakout.persistent-selections.json");

    private static MummyPerkStoreRoot? _root;
    private static string _profileKey = DefaultProfileKey;

    internal static void Initialize()
    {
        lock (Sync)
        {
            _root ??= Load();
            if (ImportLegacySelections(_root))
            {
                Save();
            }
        }
    }

    internal static void CaptureProfile(ClientCache? clientCache)
    {
        if (clientCache is null)
        {
            return;
        }

        var playerId = clientCache.UserWebPlayer?.BaseData?.PlayerId
            ?? clientCache.UserInfo?.UserId
            ?? 0;
        if (playerId == 0)
        {
            return;
        }

        lock (Sync)
        {
            _profileKey = $"user:{playerId}";
        }
    }

    internal static bool IsAllowedPassive(SkillType skillType)
    {
        return skillType is SkillType.ReaperHelloThere
            or SkillType.ReaperDontStop
            or SkillType.ReaperConnection
            or SkillType.ReaperOtherWorld
            or SkillType.ReaperTooGoodForYou;
    }

    internal static bool TryGetSkills(out SimplifiedSkillsRuntime skills)
    {
        lock (Sync)
        {
            _root ??= Load();
            skills = default;
            if (!_root.Profiles.TryGetValue(_profileKey, out var selection))
            {
                return false;
            }

            skills.PassiveSkill1 = ToRuntimeSkill(selection.PassiveSkill1);
            skills.PassiveSkill2 = ToRuntimeSkill(selection.PassiveSkill2);
            skills.PassiveSkill3 = ToRuntimeSkill(selection.PassiveSkill3);
            return true;
        }
    }

    internal static void SaveSkills(SimplifiedSkillsRuntime skills)
    {
        lock (Sync)
        {
            _root ??= Load();
            _root.Profiles[_profileKey] = new MummyPerkSelection
            {
                PassiveSkill1 = ToPersistedSkill(skills.PassiveSkill1),
                PassiveSkill2 = ToPersistedSkill(skills.PassiveSkill2),
                PassiveSkill3 = ToPersistedSkill(skills.PassiveSkill3),
            };
            Save();
        }
    }

    private static int? ToPersistedSkill(PlayerSkillRuntime skill)
    {
        return IsAllowedPassive(skill.SkillType)
            ? (int)skill.SkillType
            : null;
    }

    private static PlayerSkillRuntime ToRuntimeSkill(int? persistedSkill)
    {
        if (!persistedSkill.HasValue)
        {
            return default;
        }

        var skillType = (SkillType)persistedSkill.Value;
        return IsAllowedPassive(skillType)
            ? new PlayerSkillRuntime(skillType, 5)
            : default;
    }

    private static MummyPerkStoreRoot Load()
    {
        try
        {
            if (!File.Exists(StoragePath))
            {
                return CreateEmptyRoot();
            }

            var content = File.ReadAllText(StoragePath);
            var root = string.IsNullOrWhiteSpace(content)
                ? null
                : JsonSerializer.Deserialize<MummyPerkStoreRoot>(content, JsonOptions);
            root ??= CreateEmptyRoot();
            root.SchemaVersion = CurrentSchemaVersion;
            root.Profiles ??= new Dictionary<string, MummyPerkSelection>();
            return root;
        }
        catch (Exception exception)
        {
            MummyUnlockRuntime.LogError("Loading Mummy perk selections failed", exception);
            return CreateEmptyRoot();
        }
    }

    private static bool ImportLegacySelections(MummyPerkStoreRoot root)
    {
        if (!File.Exists(LegacyUnlockEverythingStoragePath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(LegacyUnlockEverythingStoragePath));
            if (!document.RootElement.TryGetProperty("Profiles", out var profiles)
                || profiles.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var changed = false;
            foreach (var profile in profiles.EnumerateObject())
            {
                if (root.Profiles.ContainsKey(profile.Name)
                    || !profile.Value.TryGetProperty("Characters", out var characters)
                    || !characters.TryGetProperty(LegacyMummyCharacterKey, out var mummy))
                {
                    continue;
                }

                var imported = new MummyPerkSelection
                {
                    PassiveSkill1 = ReadAllowedSkill(mummy, "PassiveSkill1"),
                    PassiveSkill2 = ReadAllowedSkill(mummy, "PassiveSkill2"),
                    PassiveSkill3 = ReadAllowedSkill(mummy, "PassiveSkill3"),
                };
                if (imported.PassiveSkill1.HasValue
                    || imported.PassiveSkill2.HasValue
                    || imported.PassiveSkill3.HasValue)
                {
                    root.Profiles[profile.Name] = imported;
                    changed = true;
                }
            }

            if (changed)
            {
                MummyUnlockRuntime.LogInfo("Imported Mummy perk selections from Unlock Everything");
            }

            return changed;
        }
        catch (Exception exception)
        {
            MummyUnlockRuntime.LogError("Importing legacy Mummy perk selections failed", exception);
            return false;
        }
    }

    private static int? ReadAllowedSkill(JsonElement selection, string propertyName)
    {
        if (!selection.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var rawSkillType))
        {
            return null;
        }

        var skillType = (SkillType)rawSkillType;
        return IsAllowedPassive(skillType) ? rawSkillType : null;
    }

    private static MummyPerkStoreRoot CreateEmptyRoot()
    {
        return new MummyPerkStoreRoot
        {
            SchemaVersion = CurrentSchemaVersion,
        };
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
            File.WriteAllText(
                StoragePath,
                JsonSerializer.Serialize(_root ?? CreateEmptyRoot(), JsonOptions));
        }
        catch (Exception exception)
        {
            MummyUnlockRuntime.LogError("Saving Mummy perk selections failed", exception);
        }
    }
}

internal sealed class MummyPerkStoreRoot
{
    public int SchemaVersion { get; set; }

    public Dictionary<string, MummyPerkSelection> Profiles { get; set; } = new();
}

internal sealed class MummyPerkSelection
{
    public int? PassiveSkill1 { get; set; }

    public int? PassiveSkill2 { get; set; }

    public int? PassiveSkill3 { get; set; }
}
