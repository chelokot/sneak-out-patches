using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using HarmonyLib;
using Types;
using UI.Views;
using UI.Views.Lobby;
using UnityEngine;
using UnityEngine.UI;

namespace SneakOut.MummyUnlock;

internal static class MummyAbilityIconRuntime
{
    private const string SarcophagusIconResourceName = "SneakOut.MummyUnlock.Assets.mummy_sarcophagus_icon.png";
    private const string TrapIconResourceName = "SneakOut.MummyUnlock.Assets.mummy_trap_icon.png";
    private const string CharacterIconResourceName = "SneakOut.MummyUnlock.Assets.mummy_character_icon.png";
    private static Texture2D? _sarcophagusTexture;
    private static Texture2D? _trapTexture;
    private static Texture2D? _characterTexture;
    private static Sprite? _sarcophagusSprite;
    private static Sprite? _trapSprite;
    private static Sprite? _characterSprite;

    public static void Initialize()
    {
    }

    public static void ApplyToCharacterShopView(CharacterShopView shopView)
    {
        ApplySprite(shopView._characterImage, GetCharacterSprite());
        ApplySprite(shopView._firstSkillImage, GetTrapSprite());
        ApplySprite(shopView._secondSkillImage, GetSarcophagusSprite());
    }

    public static void ApplyToSeekerSelectionView(SeekerSelectionView view)
    {
        var selectionImages = view._selectionsImages;
        var selectionIndices = view._selectionIndices;
        var viewModel = GetSeekerSelectionViewModel(view);
        var availableSeekers = viewModel?.AvailableSeekers;
        if (selectionImages is null || selectionIndices is null || availableSeekers is null)
        {
            return;
        }

        var visibleCount = Math.Min(selectionImages.Count, selectionIndices.Count);
        for (var imageIndex = 0; imageIndex < visibleCount; imageIndex++)
        {
            var seekerIndex = selectionIndices[imageIndex];
            if (seekerIndex < 0 || seekerIndex >= availableSeekers.Length)
            {
                continue;
            }

            if (availableSeekers[seekerIndex] != CharacterType.murderer_mummy)
            {
                continue;
            }

            ApplySprite(selectionImages[imageIndex], GetCharacterSprite());
        }
    }

    public static void ApplyToPlayerActionsView(PlayerActionsView playerActionsView, SpookedSkillType skillType, bool secondSkill)
    {
        var sprite = skillType switch
        {
            SpookedSkillType.MummySandTrap => GetTrapSprite(),
            SpookedSkillType.MummySarcophagus => GetSarcophagusSprite(),
            _ => null
        };
        if (sprite is null)
        {
            return;
        }

        var image = secondSkill ? playerActionsView._secondSkillSprite : playerActionsView._firstSkillSprite;
        ApplySprite(image, sprite);
    }

    private static void ApplySprite(Image? image, Sprite sprite)
    {
        if (image is null)
        {
            return;
        }

        image.sprite = sprite;
        image.overrideSprite = sprite;
        image.preserveAspect = true;
        image.enabled = true;
    }

    private static Sprite GetSarcophagusSprite()
    {
        return _sarcophagusSprite ??= CreateSprite(ref _sarcophagusTexture, SarcophagusIconResourceName, "MummySarcophagusAbilityIcon");
    }

    private static Sprite GetTrapSprite()
    {
        return _trapSprite ??= CreateSprite(ref _trapTexture, TrapIconResourceName, "MummyTrapAbilityIcon");
    }

    public static Sprite GetCharacterSprite()
    {
        return _characterSprite ??= CreateSprite(ref _characterTexture, CharacterIconResourceName, "MummyCharacterIcon");
    }

    private static Sprite CreateSprite(ref Texture2D? cachedTexture, string resourceName, string spriteName)
    {
        var texture = cachedTexture ??= CreateTexture(resourceName, spriteName);
        var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = spriteName;
        return sprite;
    }

    private static Texture2D CreateTexture(string resourceName, string textureName)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = textureName
        };
        if (!ImageConversion.LoadImage(texture, ToIl2CppArray(LoadRequiredBytes(resourceName))))
        {
            throw new InvalidOperationException($"Failed to decode mummy ability icon '{resourceName}'");
        }

        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        return texture;
    }

    private static SeekerSelectionViewModel? GetSeekerSelectionViewModel(SeekerSelectionView view)
    {
        for (var currentType = view.GetType(); currentType is not null; currentType = currentType.BaseType)
        {
            var viewModelField = AccessTools.Field(currentType, "ViewModel");
            if (viewModelField is not null)
            {
                return viewModelField.GetValue(view) as SeekerSelectionViewModel;
            }
        }

        return null;
    }

    private static byte[] LoadRequiredBytes(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found");
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    private static Il2CppStructArray<byte> ToIl2CppArray(IReadOnlyList<byte> values)
    {
        var result = new Il2CppStructArray<byte>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            result[index] = values[index];
        }

        return result;
    }
}
