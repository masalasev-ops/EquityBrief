using System.Reflection;
using EquityBrief.Core.Components;
using ComponentAccess = EquityBrief.Core.Components.ComponentAccess;

namespace EquityBrief.Tests.Harness;

internal sealed record DeclaredComponent(string Name, string Assembly, ComponentAccess Access);

// The components the repository ships, found by their type rather than by a list.
//
// Discovery is keyed on IComponent, not on the spelling of a property, which is
// the one thing this does differently from CheckReach inside the suite. That one
// looks for a static property literally named "Reach", so renaming it empties the
// population and nothing says so. A type that implements this interface cannot
// then be missing its member, and a component that forgets to implement it is
// caught by the catalogue direction rather than by silence.
internal static class ShippedComponents
{
    // Every assembly the repository ships, found beside the suite's own output
    // rather than named in a list here, so a component in a project nobody
    // thought to add is still found. AppDomain.GetAssemblies would return only
    // what has been loaded, which is a scope that depends on which test ran first.
    internal static IReadOnlyList<Assembly> Assemblies(int floor = 5)
    {
        var found = Directory
            .GetFiles(AppContext.BaseDirectory, "EquityBrief.*.dll")
            .Where(path => Path.GetFileNameWithoutExtension(path) != "EquityBrief.Tests")
            .Select(Assembly.LoadFrom)
            .DistinctBy(assembly => assembly.GetName().Name)
            .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();

        return found.Length >= floor
            ? found
            : throw new InvalidOperationException(
                $"Found {found.Length} shipped assemblies beside the suite, expected at least {floor}. " +
                "Scanning fewer than the repository ships would report a scope it never had, and " +
                "every component in an assembly nobody loaded would read as one that does not exist.");
    }

    internal static IReadOnlyList<DeclaredComponent> All() =>
        Assemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(IComponent)))
            .Select(type => new DeclaredComponent(type.Name, type.Assembly.GetName().Name!, Read(type)))
            .OrderBy(component => component.Name, StringComparer.Ordinal)
            .ToArray();

    static ComponentAccess Read(Type type) =>
        (ComponentAccess?)type
            .GetProperty(nameof(IComponent.Access), BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null)
        ?? throw new InvalidOperationException(
            $"{type.Name} implements IComponent and its Access could not be read. Skipping a " +
            "component whose declaration cannot be read is the under-reporting this check exists " +
            "to avoid, so this fails instead.");
}
