namespace SneakOut.LockerStunFix;

internal static class LockerBooPolicy
{
    public static bool CanArmBoo(bool lockerIsOpenAtExitStart)
    {
        return !lockerIsOpenAtExitStart;
    }
}
