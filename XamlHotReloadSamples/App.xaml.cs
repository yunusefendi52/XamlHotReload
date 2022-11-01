using System.Collections.Concurrent;
using System.Reflection;
using XamlHotReloadSamples.Pages;

namespace XamlHotReloadSamples;

public partial class App : Application
{
    public App()
    {
#if DEBUG
        XamlHotReload.Reloader.Instance.Init("http://localhost");
        var views = new ConcurrentBag<VisualElement>();
        XamlHotReload.Reloader.Instance.OnInterceptInstance += (s, e) =>
        {
            if (e.Instance is VisualElement visualElement)
            {
                views.Add(visualElement);
            }
        };
        XamlHotReload.Reloader.Instance.OnNewAssembly += (s, newAssembly) =>
        {
            var reloadAll = true;
            if (reloadAll)
            {
                foreach (var view in views)
                {
                    var className = view.GetType().FullName;
                    var newViewType = newAssembly.GetType(className);
                    if (Activator.CreateInstance(newViewType) is VisualElement newView)
                    {
                        Application.Current.MainPage.Dispatcher.Dispatch(() =>
                        {
                            if (view is ContentPage cp && newView is ContentPage newCp)
                            {
                                cp.Content = newCp.Content;
                            }
                            else if (view is ContentView cv && newView is ContentView newCv)
                            {
                                cv.Content = newCv.Content;
                            }
                        });
                    }
                }
            }
        };
#endif

        InitializeComponent();

        MainPage = new NavigationPage(new MainPage());
    }
}