using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace MediaOrcestrator.Domain.Migration;

public sealed class WindowsShortcutCreator : IShortcutCreator
{
    public void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string? description = null,
        string? iconPath = null,
        int iconIndex = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Создание ярлыков .lnk поддерживается только в Windows.");
        }

        var fullShortcutPath = Path.GetFullPath(shortcutPath);
        var shortcutDir = Path.GetDirectoryName(fullShortcutPath);
        if (!string.IsNullOrWhiteSpace(shortcutDir))
        {
            Directory.CreateDirectory(shortcutDir);
        }

        var shellLink = (object)new ShellLink();

        try
        {
            var link = (IShellLinkW)shellLink;
            link.SetPath(Path.GetFullPath(targetPath));
            link.SetWorkingDirectory(Path.GetFullPath(workingDirectory));

            if (!string.IsNullOrWhiteSpace(description))
            {
                link.SetDescription(description);
            }

            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                link.SetIconLocation(Path.GetFullPath(iconPath), iconIndex);
            }

            var persistFile = (IPersistFile)shellLink;
            persistFile.Save(fullShortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
