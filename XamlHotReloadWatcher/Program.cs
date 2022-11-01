// See https://aka.ms/new-console-template for more information

using System.CommandLine;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using static SimpleExec.Command;

var command = new RootCommand("Maui XAML hot reload watcher");

var folderOption = new Option<DirectoryInfo>("--folder", () => new DirectoryInfo(Directory.GetCurrentDirectory()),
    "Folder to watch");
command.Add(folderOption);

var uriOption = new Option<Uri[]>("--device-urls", () => new[] { new Uri("http://localhost:7451") }, "Device address")
{
    AllowMultipleArgumentsPerToken = true,
};
command.Add(uriOption);

var handler = new SocketsHttpHandler();

command.SetHandler((folder, deviceUrls) =>
{
    var exitWait = new TaskCompletionSource<int>();

    var fullDir = folder.FullName;

    Console.WriteLine($"Watching started {fullDir}, device urls: {string.Join(", ", deviceUrls.Select(v => v.ToString()))}");
    Console.WriteLine($"PID: {Environment.ProcessId}");

    async Task SendChanges(string fullPath)
    {
        foreach (var deviceUrl in deviceUrls)
        {
            try
            {
                Console.WriteLine($"Sending changes to {deviceUrl}: {fullPath}");

                if (fullPath.EndsWith(".cs"))
                {
                    var outputDir = Directory.CreateDirectory(
                        Path.Combine("bin",
                        "templibs")
                    ).FullName;
                    await RunAsync("dotnet", $"build -f net6.0 -p:OutputPath={outputDir}", folder.FullName);

                    var dllFile = Path.Combine(outputDir, "XamlHotReloadSamples.dll");
                    using var http = new HttpClient(handler, disposeHandler: false)
                    {
                        BaseAddress = deviceUrl,
                    };
                    var response = await http.PostAsync(
                        "/upload-assembly",
                        new ByteArrayContent(File.ReadAllBytes(dllFile)));
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Error upload assembly");
                    }
                    else
                    {
                        Console.WriteLine("uploaded assembly");
                    }
                }
                else
                {
                    using var http = new HttpClient(handler, disposeHandler: false)
                    {
                        BaseAddress = deviceUrl,
                    };
                    var xaml = File.ReadAllText(fullPath);
                    var response = await http.PostAsync(
                        "/upload-xaml",
                        new StringContent(xaml, Encoding.UTF8, "text/xml"));
                    var content = await response.Content.ReadAsStringAsync();
                    var contentJson = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content);
                    if (contentJson == null)
                        continue;
                    var reloaded = contentJson["Reloaded"].GetBoolean();
                    if (reloaded)
                    {
                        Console.WriteLine($"Reloaded {deviceUrl} {fullPath}: {reloaded}");
                        continue;
                    }
                    Console.WriteLine($"Changes not reloaded {deviceUrl}");
                    var errorException = contentJson["Exception"].GetString();
                    Console.WriteLine(errorException);
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                Console.WriteLine($"Error sending changes to {deviceUrl}, {ex}");
            }
        }
    }

    var watcher = new FileSystemWatcher(fullDir)
    {
        Filters = { "*.xaml", /*"*.cs"*/ },
        IncludeSubdirectories = true,
        EnableRaisingEvents = true,
    };
    watcher.NotifyFilter = NotifyFilters.Attributes | NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.Size;
    watcher.Changed += async (s, e) =>
    {
        await SendChanges(e.FullPath);
    };
    watcher.Created += async (s, e) =>
    {
        await SendChanges(e.FullPath);
    };
    watcher.Error += (s, e) =>
    {
        Console.WriteLine(e.GetException());
        exitWait.TrySetResult(1);
    };

    var autoResetEvent = new AutoResetEvent(false);

    Console.CancelKeyPress += (s, e) =>
    {
        e.Cancel = true;
        autoResetEvent.Set();
    };

    autoResetEvent.WaitOne();
    Console.WriteLine("Disposing");
    watcher.Dispose();
    exitWait.TrySetResult(0);
    Console.WriteLine("Disposed");
}, folderOption, uriOption);

return await command.InvokeAsync(args);
