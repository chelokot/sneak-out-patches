using Base;

namespace SneakOut.UnlockEverything;

internal static class UnlockEverythingOverlay
{
    public static void EnsureClientCache(ClientCache clientCache)
    {
        if (UnlockEverythingRuntime.UseLocalStub)
        {
            UnlockEverythingStub.PopulateClientCache(clientCache);
            return;
        }

        if (UnlockEverythingRuntime.UseProfileOverlay)
        {
            UnlockEverythingStub.ApplyProfileOverlay(clientCache);
        }
    }
}
