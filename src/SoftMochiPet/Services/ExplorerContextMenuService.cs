using Microsoft.Win32;
using SoftMochiPet.Interop;

namespace SoftMochiPet.Services;

public static class ExplorerContextMenuService
{
    private const string VerbName = "SoftMochiPetFeed";
    private const string MigrationKeyPath = @"Software\SoftMochiPet";
    private const string MigrationValueName = "LegacyContextMenuRemoved";
    private static readonly string[] ItemClasses = ["*", "Directory"];

    public static bool UninstallOnce()
    {
        using var migrationKey = Registry.CurrentUser.CreateSubKey(MigrationKeyPath);
        if (migrationKey?.GetValue(MigrationValueName) is int completed && completed == 1)
        {
            return false;
        }

        Uninstall();
        migrationKey?.SetValue(MigrationValueName, 1, RegistryValueKind.DWord);
        return true;
    }

    private static void Uninstall()
    {
        foreach (var itemClass in ItemClasses)
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Classes\{itemClass}\shell\{VerbName}",
                throwOnMissingSubKey: false);
        }

        NativeMethods.SHChangeNotify(
            NativeMethods.ShcneAssocChanged,
            NativeMethods.ShcnfIdList,
            IntPtr.Zero,
            IntPtr.Zero);
    }
}
