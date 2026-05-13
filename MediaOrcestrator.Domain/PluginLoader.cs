using MediaOrcestrator.Modules;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace MediaOrcestrator.Domain;

public static class PluginLoader
{
    public static void Configure(IServiceCollection services, string pluginPath)
    {
        if (!Directory.Exists(pluginPath))
        {
            return;
        }

        var assemblies = LoadAssemblies(pluginPath);
        var assembliesWithModule = new HashSet<Assembly>();

        foreach (var assembly in assemblies)
        {
            var moduleTypes = SafeGetTypes(assembly)
                .Where(IsConcreteImplementationOf<IPluginModule>);

            foreach (var moduleType in moduleTypes)
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

            foreach (var type in SafeGetTypes(assembly).Where(IsConcreteImplementationOf<ISourceType>))
            {
                services.AddSingleton(typeof(ISourceType), type);
            }
        }
    }

    private static bool IsConcreteImplementationOf<T>(Type type)
    {
        return type is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(type);
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            Console.WriteLine($"Ошибка загрузки типов из {assembly.FullName}: {ex.Message}");
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private static List<Assembly> LoadAssemblies(string directoryPath)
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
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось загрузить {dll}: {ex.Message}");
            }
        }

        return assemblies;
    }
}
