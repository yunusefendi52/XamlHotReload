using System.CodeDom.Compiler;
using System.Net;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using Microsoft.Maui.Controls;
using Extensions = Microsoft.Maui.Controls.Xaml.Extensions;
using Microsoft.Maui.Controls.Internals;

namespace XamlHotReload;

public class Reloader
{
    static Reloader instance;
    public static Reloader Instance => instance ??= new();

    public event EventHandler<string> ReceiveXaml;

    public void RaiseReceiveXaml(string xaml) => ReceiveXaml?.Invoke(this, xaml);

    public event EventHandler<(string MethodName, object Instance)> OnInterceptInstance;

    public event EventHandler<Assembly> OnNewAssembly; 

    public void RaiseNewAssembly(Assembly assembly)
    {
        OnNewAssembly?.Invoke(this, assembly);
    }

    bool initialized;

    public void Init(string address = "http://*", int port = 7451)
    {
        if (initialized)
            return;
        initialized = true;

        OnInterceptInstance += (s, e) =>
        {
            if (e.Instance is not VisualElement view)
                return;
            var viewClass = view.GetType().FullName;
            if (viewsXaml.ContainsKey(viewClass))
            {
                var xaml = viewsXaml[viewClass];
                ReloadView(view, xaml);
            }

            Register(view);
        };

        ReceiveXaml += (s, e) =>
        {
            var r = new System.Xml.XmlTextReader(new StringReader(e));
            r.XmlResolver = new Resolver();
            r.DtdProcessing = DtdProcessing.Ignore;
            var doc = XDocument.Load(r).Document;
            var @class = doc.Root.Attributes().First(v => v.Name.LocalName.Contains("Class")).Value;
            viewsXaml[@class] = e;
            OnReload?.Invoke(this, (@class, e));
        };

        Task.Run(async () =>
        {
            try
            {
                var url = $"{address}:{port}";
                var server = new WebServer(o => o
                            .WithUrlPrefix(url)
                            .WithMode(HttpListenerMode.EmbedIO))
                        .WithWebApi("/", m => m
                            .WithController<ReloaderController>());

                server.StateChanged += (s, e) =>
                {
                    Console.WriteLine($"HotReload state - {e.NewState} - {url}");
                };
                await server.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HotReload: {ex}");
            }
        });
    }

    readonly Dictionary<string, string> viewsXaml = new();

    public event EventHandler<(string XClass, string Xaml)> OnReload;

    void Register(VisualElement view)
    {
        var weakView = new WeakReference(view);
        OnReload += (sender, xaml) =>
        {
            if (!weakView.IsAlive)
                return;
            if (weakView.Target is not VisualElement view)
            {
                Console.WriteLine("Reloaded xaml is not view");
                return;
            }
            if (view.GetType().FullName != xaml.XClass)
                return;
            ReloadView(view, xaml.Xaml);
        };
    }

    void ReloadView(VisualElement view, string xaml)
    {
        Application.Current?.Dispatcher?.Dispatch(() =>
        {
            try
            {
                var scopeNames = new List<string>();
                if (NameScope.GetNameScope(view) is INameScope nameScope)
                {
                    var nameScopeType = nameScope.GetType();
                    var _names =
                        nameScopeType.GetRuntimeFields().FirstOrDefault(v => v.Name == "_names")?.GetValue(nameScope) as
                            Dictionary<string, object>;
                    scopeNames = _names.Select(v => v.Key).ToList();
                    foreach (var name in _names)
                    {
                        nameScope.UnregisterName(name.Key);
                    }
                }

                view.Resources?.Clear();

                var context = view.BindingContext;
                if (view != null)
                    view.BindingContext = null;
                Extensions.LoadFromXaml(view, xaml);
                if (view != null)
                    view.BindingContext = context;

                // x:Modifier = private (default)
                var fields = view.GetType().GetRuntimeFields();
                foreach (var field in fields)
                {
                    if (scopeNames.Contains(field.Name))
                    {
                        var newNameScope = NameScope.GetNameScope(view);
                        var fieldObj = newNameScope.FindByName(field.Name);
                        field.SetValue(view, fieldObj);
                        var fieldObjReflection = field.GetValue(view);
                        if (!ReferenceEquals(fieldObj, fieldObjReflection))
                        {
                            Console.WriteLine("HotReload: Different reference?");
                        }
                        scopeNames.Remove(field.Name);
                    }
                }

                // x:Modifier = Public
                var properties = view.GetType().GetRuntimeProperties();
                foreach (var prop in properties)
                {
                    if (scopeNames.Contains(prop.Name))
                    {
                        var newNameScope = NameScope.GetNameScope(view);
                        var p = newNameScope.FindByName(prop.Name);
                        prop.SetValue(obj: view, p);
                        var objRef = prop.GetValue(view);
                        if (!ReferenceEquals(p, objRef))
                        {
                            Console.WriteLine("HotReload: Different reference?");
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine($"HotReload: LoadFromXaml Error: {exception} {view.GetType().FullName}");
            }
        });
    }

    public void TryInterceptInstance(object view)
    {
        OnInterceptInstance?.Invoke(view, ("", view));
    }
}

class ReloaderController : WebApiController
{
    [Route(HttpVerbs.Post, "/upload-xaml")]
    public async Task UploadXaml()
    {
        try
        {
            var xaml = await HttpContext.GetRequestBodyAsStringAsync();
            Reloader.Instance.RaiseReceiveXaml(xaml);

            await ResponseSerializer.Json(HttpContext, new
            {
                Reloaded = true,
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HotReload: {ex}");

            await ResponseSerializer.Json(HttpContext, new
            {
                Reloaded = false,
                Exception = ex.ToString(),
            });
        }
    }

    [Route(HttpVerbs.Post, "/upload-assembly")]
    public async Task UploadAssembly()
    {
        try
        {
            var data = await HttpContext.GetRequestBodyAsByteArrayAsync();
            var newAssembly = Assembly.Load(data);

            Reloader.Instance.RaiseNewAssembly(newAssembly);

            await ResponseSerializer.Json(HttpContext, new
            {
                Uploaded = true,
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HotReload: {ex}");

            HttpContext.Response.StatusCode = 500;
            await ResponseSerializer.Json(HttpContext, new
            {
                Reloaded = false,
                Exception = ex.ToString(),
            });
        }
    }
}

class Resolver : System.Xml.XmlResolver
{
    public override Uri ResolveUri(Uri baseUri, string relativeUri)
    {
        return baseUri;
    }

    public override object GetEntity(Uri absoluteUri, string role, Type type)
    {
        return null;
    }

    public override ICredentials Credentials
    {
        set { }
    }
}
