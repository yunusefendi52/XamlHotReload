#!/usr/bin/env dotnet dotnet-script
#r "nuget: Mono.Cecil, 0.11.4"
using System.Net;

using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class ReloadInjector
{
    public static bool Inject(
        AssemblyDefinition assembly,
        Action<string> debug = null,
        Action<string> info = null
    )
    {
        info?.Invoke($"Start injecting reload {assembly}");

        var module = assembly.MainModule;
        var allTypeMethods = module.Types.SelectMany(v => v.Methods).ToList();

        var reloaderAssemblyRef = module.AssemblyReferences.First(v => v.Name == "XamlHotReload");
        var reloader = module.AssemblyResolver.Resolve(reloaderAssemblyRef);

        foreach (var method in allTypeMethods)
        {
            if (method.Body == null || method.Body.Instructions == null || !method.Body.Instructions.Any())
                continue;

            var lastInstr = method.Body.Instructions.Last();
            method.Body.Instructions.RemoveAt(method.Body.Instructions.Count - 1);
            
            // Inject at the end of method
            var il = method.Body.GetILProcessor();

            var reloaderType = reloader.MainModule.Types.First(v => v.FullName.Contains("XamlHotReload.Reloader"));
            var reloaderInstance = reloaderType.Methods.First(v => v.Name.Contains("get_Instance"));
            il.Emit(OpCodes.Call, module.ImportReference(reloaderInstance));
            il.Emit(OpCodes.Ldarg_0);
            var tryInitComponent = reloaderType.Methods.First(v => v.Name.Contains("TryInterceptInstance"));
            il.Emit(OpCodes.Callvirt, module.ImportReference(tryInitComponent));
            il.Emit(OpCodes.Nop);
            il.Emit(OpCodes.Ret);
        }

        return true;
    }
}

class AssemblyResolver : BaseAssemblyResolver
{
    // private readonly ILogger _logger;
    private readonly IDictionary<string, AssemblyDefinition> _assemblyCache;
    private readonly IDictionary<string, TypeDefinition> _typeCache;

    public AssemblyResolver(IEnumerable<string> assembliesToInclude)
    {
        // _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _assemblyCache = new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);
        _typeCache = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
        AddSearchDirectory(Path.GetFullPath("."));
        AddSearchDirectory(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
        ResolveFailure += OnResolveFailure;
        foreach (var assemblyPath in assembliesToInclude)
        {
            AssemblyDefinition assembly = GetFromPath(assemblyPath);
            if (assembly != null)
            {
                // logger.Debug($"Caching ref '{assembly.Name.FullName}' from '{assembly.MainModule.FileName}'", DebugLogLevel.Verbose);
                _assemblyCache[assembly.Name.Name] = assembly;
            }
        }
        
        // logger.Info("Done loading referenced assemblies");
    }

    public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters readParameters)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));

        if (_assemblyCache.TryGetValue(name.Name, out AssemblyDefinition assemblyDefinition))
        {
            // _logger.Debug($"Loaded assembly {name.FullName} from cache", DebugLogLevel.Verbose);
            return assemblyDefinition;
        }
        assemblyDefinition = base.Resolve(name, readParameters);
        
        if (assemblyDefinition != null)
        {
            // _logger.Debug($"Resolved assembly {name.FullName} from '{assemblyDefinition.MainModule.FileName}'", DebugLogLevel.Verbose);
            _assemblyCache[name.Name] = assemblyDefinition;
        }
        else
        {
            // _logger.Info($"Could not find {name.FullName}");
        }
        return assemblyDefinition;
    }

    public TypeDefinition ResolveType(string fullTypeName)
    {
        if (fullTypeName is null) throw new ArgumentNullException(nameof(fullTypeName));
        if (_typeCache.TryGetValue(fullTypeName, out TypeDefinition type))
        {
            return type;
        }

        foreach (AssemblyDefinition assembly in _assemblyCache.Values)
        {
            type = assembly.MainModule.GetType(fullTypeName);
            if (type != null)
            {
                return _typeCache[fullTypeName] = type;
            }
        }
        return null;
    }

    protected override void Dispose(bool disposing)
    {
        foreach (AssemblyDefinition assemblyDefinition in _assemblyCache.Values)
        {
            assemblyDefinition?.Dispose();
        }
        _assemblyCache.Clear();
        base.Dispose(disposing);
    }

    private AssemblyDefinition OnResolveFailure(object sender, AssemblyNameReference reference)
    {
        Assembly assembly;
        try
        {
            if (_assemblyCache.TryGetValue(reference.Name, out AssemblyDefinition cached))
            {
                return cached;
            }
#pragma warning disable 618
            assembly = Assembly.LoadWithPartialName(reference.Name);
#pragma warning restore 618
        }
        catch (FileNotFoundException)
        {
            // _logger.Warning($"Failed to resolve '{reference.Name}'");
            assembly = null;
        }

        if (!string.IsNullOrWhiteSpace(assembly?.CodeBase))
        {
            string path = new Uri(assembly.CodeBase).AbsolutePath;
            return GetFromPath(path);
        }

        return null;
    }

    private AssemblyDefinition GetFromPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(Uri.UnescapeDataString(filePath)))
            {
                filePath = Uri.UnescapeDataString(filePath);
            }
            else
            {
                // _logger.Warning($"Could not find assembly '{filePath}'");
                return null;
            }
        }
        var readerParameters = new ReaderParameters(ReadingMode.Deferred)
        {
            ReadWrite = false,
            ReadSymbols = false,
            AssemblyResolver = this
        };
        // _logger.Debug($"Loading '{filePath}'", DebugLogLevel.Verbose);
        return AssemblyDefinition.ReadAssembly(filePath, readerParameters);
    }
}

var assemblyPath = Args[0];
var references = Args.ElementAtOrDefault(1);

var resolver = new AssemblyResolver(references.Split(";"));
var assemblyDef = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters
{
    ReadWrite = true,
    AssemblyResolver = resolver,
});
bool loadedSymbols;
try
{
    assemblyDef.MainModule.ReadSymbols();
    loadedSymbols = true;
}
catch
{
    loadedSymbols = false;
}
if (!ReloadInjector.Inject(assemblyDef, (v) =>
{
    Console.WriteLine(v);
},
(v) =>
{
    Console.WriteLine(v);
}))
{
    return 1;
}
assemblyDef.Write(new WriterParameters
{
    WriteSymbols = loadedSymbols,
});
