using System.Reflection;
using System.Runtime.Loader;

var assemblyPath = Path.GetFullPath(args.Length > 0 ? args[0] : throw new ArgumentException("assembly path required"));
var pattern = args.Length > 1 ? args[1] : "PortalPlayView";
var assemblyDirectory = Path.GetDirectoryName(assemblyPath) ?? throw new InvalidOperationException("assembly directory missing");
var coreDirectory = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "core"));
var candidateDirectories = new[] { assemblyDirectory, coreDirectory };

AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
{
    foreach (var directory in candidateDirectories)
    {
        var candidatePath = Path.Combine(directory, $"{assemblyName.Name}.dll");
        if (File.Exists(candidatePath))
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidatePath);
        }
    }

    return null;
};

var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
IEnumerable<Type> types;

try
{
    types = assembly.GetTypes();
}
catch (ReflectionTypeLoadException exception)
{
    types = exception.Types.Where(type => type is not null)!;
    foreach (var loaderException in exception.LoaderExceptions.Where(loaderException => loaderException is not null))
    {
        Console.Error.WriteLine(loaderException!.Message);
    }
}

foreach (var type in types
    .Where(type => type.FullName is not null && type.FullName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
    .OrderBy(type => type.FullName))
{
    Console.WriteLine($"TYPE {type.FullName}");
    Console.WriteLine($"  BASE {type.BaseType?.FullName}");
    foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
    {
        Console.WriteLine($"  PROPERTY {property.PropertyType.FullName} {property.Name}");
    }
    foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
    {
        Console.WriteLine($"  FIELD {field.FieldType.FullName} {field.Name}");
    }

    foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).OrderBy(method => method.Name))
    {
        var parameters = string.Join(", ", method.GetParameters().Select(parameter => $"{parameter.ParameterType.FullName} {parameter.Name}"));
        Console.WriteLine($"  METHOD {method.ReturnType.FullName} {method.Name}({parameters})");
    }
}
