using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Types;
using UI.Views;
using UI.Views.Lobby;
using UnityEngine;
using UnityEngine.UI;

namespace SneakOut.MummyUnlock;

internal static class MummyAbilityIconRuntime
{
    private const string CharacterIconResourceName = "SneakOut.MummyUnlock.Assets.mummy_character_icon.png";
    private static Texture2D? _characterTexture;
    private static Sprite? _sarcophagusSprite;
    private static Sprite? _trapSprite;
    private static Sprite? _characterSprite;

    public static void ApplyToCharacterShopView(CharacterShopView shopView)
    {
        ApplySprite(shopView._characterImage, GetCharacterSprite());
    }

    public static void ApplyToCharacterShopCarousel(CharacterShopView shopView)
    {
        var characterAvatars = shopView._characterAvatars;
        var indicesToView = shopView._indiciesToView;
        var charactersToBuy = shopView._charactersToBuy;
        if (characterAvatars is null || indicesToView is null || charactersToBuy is null)
        {
            return;
        }

        var visibleCount = Math.Min(characterAvatars.Length, indicesToView.Length);
        for (var avatarIndex = 0; avatarIndex < visibleCount; avatarIndex++)
        {
            var characterIndex = indicesToView[avatarIndex];
            if (characterIndex < 0 || characterIndex >= charactersToBuy.Length)
            {
                continue;
            }

            if (charactersToBuy[characterIndex].CharacterType != CharacterType.murderer_mummy)
            {
                continue;
            }

            ApplyCarouselSprite(characterAvatars[avatarIndex], GetCharacterSprite());
        }
    }

    public static void ApplyToSeekerSelectionView(SeekerSelectionView view)
    {
        var selectionImages = view._selectionsImages;
        var selectionIndices = view._selectionIndices;
        var viewModel = view.ViewModel;
        if (viewModel is null)
        {
            return;
        }

        var availableSeekers = viewModel.AvailableSeekers;
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

            ApplyCarouselSprite(selectionImages[imageIndex], GetCharacterSprite());
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

    private static void ApplyCarouselSprite(Image? image, Sprite sprite)
    {
        if (image is null)
        {
            return;
        }

        image.overrideSprite = null;
        image.sprite = sprite;
        image.preserveAspect = true;
        image.enabled = true;
    }

    private static Sprite GetSarcophagusSprite()
    {
        return _sarcophagusSprite ??= ResolveRequiredSprite("Sarcophagus");
    }

    private static Sprite GetTrapSprite()
    {
        return _trapSprite ??= ResolveRequiredSprite("Mummy_sandtrap");
    }

    public static Sprite GetCharacterSprite()
    {
        return _characterSprite ??= CreateSprite(ref _characterTexture, CharacterIconResourceName, "MummyCharacterIcon");
    }

    private static Sprite ResolveRequiredSprite(string spriteName)
    {
        return Resources.FindObjectsOfTypeAll<Sprite>()
                   .FirstOrDefault(sprite => sprite.name == spriteName)
               ?? throw new InvalidOperationException($"Required game sprite '{spriteName}' was not loaded");
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
