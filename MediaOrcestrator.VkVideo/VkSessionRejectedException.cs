using MediaOrcestrator.Modules;

namespace MediaOrcestrator.VkVideo;

internal sealed class VkSessionRejectedException()
    : SourceSessionRejectedException("VK отклонил сохранённую сессию. Выполните вход заново.")
{
}
