using MediaOrcestrator.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace MediaOrcestrator.Domain;

public static class PluginLoader
{
    public static void Configure(IServiceCollection services, string pluginPath, ILogger? logger = null)
    {
        if (!Directory.Exists(pluginPath))
        {
            return;
        }

        var assemblies = LoadAssemblies(pluginPath, logger);
        var assembliesWithModule = new HashSet<Assembly>();
        var typesByAssembly = new Dictionary<Assembly, Type[]>();

        foreach (var assembly in assemblies)
        {
            if (!typesByAssembly.TryGetValue(assembly, out var types))
            {
                types = SafeGetTypes(assembly, logger);
                typesByAssembly.Add(assembly, types);
            }

            foreach (var moduleType in types.Where(IsConcreteImplementationOf<IPluginModule>))
            {
                if (Activator.CreateInstance(moduleType) is not IPluginModule module)
                {
                    continue;
                }

                module.Register(services);
                assembliesWithModule.Add(assembly);
            }
        }

        foreach (var assembly in assemblies)
        {
            if (assembliesWithModule.Contains(assembly))
            {
                continue;
            }

            foreach (var type in typesByAssembly[assembly].Where(IsConcreteImplementationOf<ISourceType>))
            {
                services.AddSingleton(typeof(ISourceType), type);
            }
        }
    }

    private static bool IsConcreteImplementationOf<T>(Type type)
    {
        return type is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(type);
    }

    private static Type[] SafeGetTypes(Assembly assembly, ILogger? logger)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            if (logger is null)
            {
                Console.WriteLine($"Ошибка загрузки типов из {assembly.FullName}: {ex.Message}");
            }
            else
            {
                logger.LogWarning(ex, "Не удалось загрузить все типы из {Assembly}: {LoaderErrors}",
                    assembly.FullName, ex.LoaderExceptions.Where(error => error is not null).Select(error => error!.Message).Distinct().ToArray());
            }
            return ex.Types.OfType<Type>().ToArray();
        }
    }

    private static List<Assembly> LoadAssemblies(string directoryPath, ILogger? logger)
    {
        var loadedByName = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => a.GetName().Name is not null)
            .GroupBy(a => a.GetName().Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var assemblies = new List<Assembly>();
        var dllFiles = Directory.GetFiles(directoryPath, "*.dll", SearchOption.AllDirectories);

        foreach (var dll in dllFiles)
        {
            try
            {
                var name = AssemblyName.GetAssemblyName(dll).Name;

                if (name is not null && loadedByName.TryGetValue(name, out var existing))
                {
                    assemblies.Add(existing);
                    continue;
                }

                var assembly = Assembly.LoadFrom(dll);
                assemblies.Add(assembly);

                if (assembly.GetName().Name is { } assemblyName)
                {
                    loadedByName[assemblyName] = assembly;
                }
            }
            catch (BadImageFormatException ex) when (logger is not null)
            {
                logger.LogDebug(ex, "Файл {Path} не удалось прочитать как сборку .NET", dll);
            }
            catch (Exception ex)
            {
                if (logger is null)
                {
                    Console.WriteLine($"Не удалось загрузить {dll}: {ex.Message}");
                }
                else
                {
                    logger.LogWarning(ex, "Не удалось загрузить сборку {Path}", dll);
                }
            }
        }

        return assemblies;
    }
}
