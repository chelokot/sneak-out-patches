using Kinguinverse.WebServiceProvider.Types_v2;
using Scriptables;
using UnityEngine;

namespace SneakOut.UnlockEverything;

internal static class UnlockEverythingCosmeticCatalog
{
    private static HashSet<SkinPartType>? _supportedSkinParts;

    public static IReadOnlySet<SkinPartType>? GetSupportedSkinParts()
    {
        if (_supportedSkinParts is not null)
        {
            return _supportedSkinParts;
        }

        var catalogs = Resources.FindObjectsOfTypeAll<SpookedSkinSprites>();
        foreach (var catalog in catalogs)
        {
            if (catalog is null || catalog.Pointer == IntPtr.Zero || catalog._skinReference is null)
            {
                continue;
            }

            var supported = new HashSet<SkinPartType>();
            foreach (var reference in catalog._skinReference)
            {
                if (reference is null
                    || reference.Pointer == IntPtr.Zero
                    || reference.SkinPartType == SkinPartType.None
                    || string.IsNullOrWhiteSpace(reference.SpriteName))
                {
                    continue;
                }

                supported.Add(reference.SkinPartType);
            }

            if (supported.Count > 0)
            {
                _supportedSkinParts = supported;
                return _supportedSkinParts;
            }
        }

        // Returning null is intentional: before the game's authoritative sprite catalog is
        // loaded, preserve backend inventory instead of inventing every enum member. A later
        // profile refresh will augment it once the catalog exists.
        return null;
    }
}
