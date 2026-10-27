using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Homepage.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Homepage.Services;

/// <summary>
/// Runs at server start: creates default sections and asks File Transformation to inject the client into index.html.
/// </summary>
public class StartupService : IScheduledTask
{
    private const string IndexTransformationId = "fe3d2200-113f-47e0-a349-9c59e6eabed7";

    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<StartupService> _logger;

    public StartupService(ILibraryManager libraryManager, ILogger<StartupService> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public string Name => "Homepage Startup";

    public string Key => "Jellyfin.Plugin.Homepage.Startup";

    public string Description => "Prépare la configuration par défaut et injecte Homepage dans le client web.";

    public string Category => "Startup Services";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        PluginConfiguration config = Plugin.Instance.Configuration;
        bool changed = DefaultsFactory.EnsureDefaults(config);
        changed |= DefaultsFactory.EnsureSections(config, _libraryManager.GetVirtualFolders());
        if (changed)
        {
            Plugin.Instance.UpdateConfiguration(config);
        }

        RegisterTransformation();
        progress.Report(100);
        return Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger };
    }

    private void RegisterTransformation()
    {
        Assembly? fileTransformation = AssemblyLoadContext.All
            .SelectMany(c => c.Assemblies)
            .FirstOrDefault(a => a.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) ?? false);

        MethodInfo? register = fileTransformation?
            .GetType("Jellyfin.Plugin.FileTransformation.PluginInterface")?
            .GetMethod("RegisterTransformation");

        if (register == null)
        {
            _logger.LogError("Homepage: the File Transformation plugin is required but was not found. Install it from its repository and restart Jellyfin.");
            return;
        }

        string json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["id"] = IndexTransformationId,
            ["fileNamePattern"] = "index.html",
            ["callbackAssembly"] = GetType().Assembly.FullName!,
            ["callbackClass"] = typeof(TransformationPatches).FullName!,
            ["callbackMethod"] = nameof(TransformationPatches.IndexHtml),
        });

        // The payload type is File Transformation's own JObject; build it through that type so we
        // never ship or bind to a second copy of Newtonsoft.Json.
        Type payloadType = register.GetParameters()[0].ParameterType;
        MethodInfo? parse = payloadType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) });
        object? payload = parse?.Invoke(null, new object[] { json });
        if (payload == null)
        {
            _logger.LogError("Homepage: could not build the File Transformation payload");
            return;
        }

        register.Invoke(null, new[] { payload });
        _logger.LogInformation("Homepage: client injection registered with File Transformation");
    }
}

/// <summary>Callbacks invoked by File Transformation (by reflection).</summary>
public static class TransformationPatches
{
    public static string IndexHtml(PatchRequestPayload payload)
    {
        string contents = payload.Contents ?? string.Empty;
        if (contents.Contains("data-homepage-plugin", StringComparison.Ordinal))
        {
            return contents;
        }

        PluginConfiguration config = Plugin.Instance.Configuration;
        string version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0";
        string baseUrl = string.IsNullOrWhiteSpace(config.AssetBaseUrl) ? "../Homepage" : config.AssetBaseUrl.TrimEnd('/');
        string bust = $"v={version}";

        string tags =
            $"<link rel=\"stylesheet\" href=\"{baseUrl}/homepage.css?{bust}\" data-homepage-plugin>" +
            $"<script defer src=\"{baseUrl}/homepage.js?{bust}\" data-homepage-plugin></script>";

        return Regex.Replace(contents, "</head>", tags + "</head>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }
}

public class PatchRequestPayload
{
    public string? Contents { get; set; }
}
