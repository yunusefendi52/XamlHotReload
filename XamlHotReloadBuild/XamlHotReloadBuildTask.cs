using Microsoft.Build.Framework;

namespace XamlHotReloadBuild;
public class XamlHotReloadBuildTask : BaseBuildTask
{
    public override bool WeaveAssembly()
    {
        if (ReloadInjector.Inject(ModuleDefinition.Assembly, v =>
        {
            Logger.LogMessage(MessageImportance.Low, v);
        }, v =>
        {
            Logger.LogMessage(MessageImportance.High, v);
        }))
        {
            return true;
        }
        return false;
    }
}
