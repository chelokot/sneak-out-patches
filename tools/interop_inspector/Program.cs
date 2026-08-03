using System.Reflection;
using System.Runtime.Loader;

var assemblyPath = Path.GetFullPath(args.Length > 0 ? args[0] : throw new ArgumentException("assembly path required"));
var pattern = args.Length > 1 ? args[1] : "PortalPlayView";
var methodMapPath = args.Length > 2 ? Path.GetFullPath(args[2]) : null;
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
Console.WriteLine($"ASSEMBLY {assembly.GetName().Name}");

if (methodMapPath is not null)
{
    var interopCommonPath = Path.Combine(coreDirectory, "Il2CppInterop.Common.dll");
    var interopCommon = AssemblyLoadContext.Default.LoadFromAssemblyPath(interopCommonPath);
    var mapType = interopCommon.GetType("Il2CppInterop.Common.Maps.MethodAddressToTokenMap", throwOnError: true)!;
    using var map = (IDisposable)Activator.CreateInstance(mapType, methodMapPath)!;
    var exactRva = pattern.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        && long.TryParse(pattern[2..], System.Globalization.NumberStyles.HexNumber, null, out var parsedRva)
            ? parsedRva
            : (long?)null;
    foreach (var entry in (System.Collections.IEnumerable)map)
    {
        var entryType = entry.GetType();
        var rva = (long)entryType.GetField("Item1")!.GetValue(entry)!;
        var method = (MethodBase)entryType.GetField("Item2")!.GetValue(entry)!;
        var declaringTypeName = method.DeclaringType?.FullName ?? string.Empty;
        if (exactRva.HasValue ? rva != exactRva.Value :
            !declaringTypeName.Contains(pattern, StringComparison.OrdinalIgnoreCase)
            && !method.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        Console.WriteLine($"RVA 0x{rva:X} TOKEN 0x{method.MetadataToken:X8} {declaringTypeName}.{method.Name}");
    }

    return;
}
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

static bool Matches(Type type, string searchPattern)
{
    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    if (searchPattern.StartsWith("=", StringComparison.Ordinal))
    {
        return string.Equals(type.FullName, searchPattern[1..], StringComparison.Ordinal);
    }
    return type.FullName?.Contains(searchPattern, StringComparison.OrdinalIgnoreCase) == true
        || type.GetProperties(flags).Any(member => member.Name.Contains(searchPattern, StringComparison.OrdinalIgnoreCase))
        || type.GetFields(flags).Any(member => member.Name.Contains(searchPattern, StringComparison.OrdinalIgnoreCase))
        || type.GetMethods(flags).Any(member => member.Name.Contains(searchPattern, StringComparison.OrdinalIgnoreCase));
}

foreach (var type in types
    .Where(type => Matches(type, pattern))
    .OrderBy(type => type.FullName))
{
    Console.WriteLine($"TYPE {type.FullName}");
    Console.WriteLine($"  BASE {type.BaseType?.FullName}");
    if (type.IsEnum)
    {
        foreach (var value in Enum.GetValues(type))
        {
            Console.WriteLine($"  ENUM {Convert.ToInt64(value)} {Enum.GetName(type, value)}");
        }
    }
    foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
    {
        var parameters = string.Join(", ", constructor.GetParameters().Select(parameter => $"{parameter.ParameterType.FullName} {parameter.Name}"));
        Console.WriteLine($"  CONSTRUCTOR {constructor.Attributes} ({parameters})");
    }
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
