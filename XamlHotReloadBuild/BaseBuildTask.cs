using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;

namespace XamlHotReloadBuild;

public abstract class BaseBuildTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem MainAssembly { get; set; } = null!;

    [Required]
    public ITaskItem[] References { get; set; } = null!;

    public TaskLoggingHelper Logger { get; set; } = null!;

    public AssemblyResolver AssemblyResolver { get; private set; } = null!;

    public ModuleDefinition ModuleDefinition { get; private set; } = null!;

    public override bool Execute()
    {
        Logger ??= new TaskLoggingHelper(this);

        using (var assemblyResolver = new AssemblyResolver(GetIncludedReferences()))
        {
            if (AssemblyResolver is null)
            {
                AssemblyResolver = assemblyResolver;
            }

            foreach (var assemblyName in GetAssembliesToInclude())
            {
                AssemblyResolver.Resolve(new AssemblyNameReference(assemblyName, null));
            }

            var readerParameters = new ReaderParameters
            {
                AssemblyResolver = AssemblyResolver,
                InMemory = true
            };
            var assemblyFile = MainAssembly.ItemSpec;
            using (ModuleDefinition = ModuleDefinition.ReadModule(assemblyFile, readerParameters))
            {
                bool loadedSymbols;
                try
                {
                    ModuleDefinition.ReadSymbols();
                    loadedSymbols = true;
                }
                catch
                {
                    loadedSymbols = false;
                }
                Logger.LogMessage($"Loaded '{assemblyFile}'");
                if (WeaveAssembly())
                {
                    Logger.LogMessage("Weaving complete - updating assembly");
                    var parameters = new WriterParameters
                    {
                        WriteSymbols = loadedSymbols,
                    };

                    ModuleDefinition.Write(assemblyFile, parameters);
                }
                else
                {
                    Logger.LogMessage("Weaving complete - no update");
                }
            }
        }

        return !Logger.HasLoggedErrors;
    }

    protected virtual IEnumerable<string> GetAssembliesToInclude()
    {
        yield return "mscorlib";
        yield return "System";
        yield return "netstandard";
        yield return "System.Collections";
    }

    IEnumerable<string> GetIncludedReferences()
    {
        if (References != null)
        {
            foreach (var reference in References.Select(v => v.ItemSpec))
            {
                yield return reference;
            }
        }
    }

    public abstract bool WeaveAssembly();
}
