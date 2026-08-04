using Kinguinverse.WebServiceProvider.Types_v2;
using Scriptables;
using UnityEngine;

namespace SneakOut.UnlockEverything;

internal static class UnlockEverythingCosmeticCatalog
{
    private static HashSet<SkinPartType>? _supportedSkinParts;

    public static IReadOnlySet<SkinPartType>? GetSupportedSkinParts()
    {
        var catalogs = Resources.FindObjectsOfTypeAll<SpookedSkinSprites>();
        var supported = _supportedSkinParts is null
            ? new HashSet<SkinPartType>()
            : new HashSet<SkinPartType>(_supportedSkinParts);

        foreach (var catalog in catalogs)
        {
            if (catalog is null || catalog.Pointer == IntPtr.Zero || catalog._skinReference is null)
            {
                continue;
            }

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
        }

        if (supported.Count > 0)
        {
            // The game stores different wardrobe categories in separate sprite catalogs.
            // Keep their union and allow it to grow as later-loaded catalogs appear; taking
            // only the first catalog made most categories sparse or completely empty.
            _supportedSkinParts = supported;
            return _supportedSkinParts;
        }

        // Returning null is intentional: before any authoritative sprite catalog is loaded,
        // preserve backend inventory instead of inventing every enum member. A later call can
        // augment the product list once the catalogs exist.
        return null;
    }
}
