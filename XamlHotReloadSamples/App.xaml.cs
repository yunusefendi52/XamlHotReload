using XamlHotReloadSamples.Pages;

namespace XamlHotReloadSamples;

public partial class App : Application
{
    public App()
    {
#if DEBUG
        XamlHotReload.Reloader.Instance.Init("http://localhost");
#endif

        InitializeComponent();

        MainPage = new NavigationPage(new MainPage());
    }
}