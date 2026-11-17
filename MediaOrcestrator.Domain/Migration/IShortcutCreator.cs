namespace MediaOrcestrator.Domain.Migration;

public interface IShortcutCreator
{
    void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string? description = null,
        string? iconPath = null,
        int iconIndex = 0);
}
