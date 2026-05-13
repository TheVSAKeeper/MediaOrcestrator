using Microsoft.Extensions.DependencyInjection;

namespace MediaOrcestrator.Modules;

/// <summary>
/// Контракт регистрации сервисов плагина в общий DI-контейнер хоста.
/// </summary>
/// <remarks>
/// Реализуется в плагинной DLL отдельным классом без аргументов в конструкторе.
/// Хост находит реализации рефлексией, создаёт через <see cref="System.Activator" />
/// и вызывает <see cref="Register" /> до построения <see cref="System.IServiceProvider" />.
/// Если в сборке найден <see cref="IPluginModule" />, автоматическое сканирование
/// <see cref="ISourceType" /> в этой сборке отключается — плагин сам отвечает
/// за регистрацию своих <see cref="ISourceType" /> и внутренних сервисов.
/// </remarks>
public interface IPluginModule
{
    /// <summary>
    /// Регистрирует сервисы плагина в общем <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">DI-коллекция, общая с хостом и другими плагинами.</param>
    void Register(IServiceCollection services);
}
