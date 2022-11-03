using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace XamlHotReloadBuild;

public static class ReloadInjector
{
    public static bool Inject(
        AssemblyDefinition assembly,
        Action<string>? debug = null,
        Action<string>? info = null
    )
    {
        info?.Invoke($"Start injecting reload {assembly}");

        var module = assembly.MainModule;
        var allTypeMethods = module.Types.SelectMany(v => v.Methods);

        var reloaderAssemblyRef = module.AssemblyReferences.FirstOrDefault(v => v.Name == "XamlHotReload");
        if (reloaderAssemblyRef == null)
        {
            foreach (var a in module.AssemblyReferences)
            {
                info?.Invoke(a.Name);
            }
            info?.Invoke($"XamlHotReload dependency not found");
            return true;
        }

        var reloader = module.AssemblyResolver.Resolve(reloaderAssemblyRef);

        foreach (var method in allTypeMethods)
        {
            if (method.Body == null)
            {
                continue;
            }
            if (method.Name != "InitializeComponent")
            {
                debug?.Invoke($"Skip method {method.Name}");
                continue;
            }

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
