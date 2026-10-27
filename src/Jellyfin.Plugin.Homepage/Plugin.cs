using Jellyfin.Plugin.Homepage.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Homepage;

/// <summary>
/// Homepage: hero, header bar, configurable home sections and Seerr integration in one plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public const string PluginGuid = "acdcaecb-228d-4e50-acfb-479d2f8a90a6";

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        DataDirectory = Path.Combine(applicationPaths.DataPath, "homepage");
        Directory.CreateDirectory(DataDirectory);

        // Fresh install: seed sections and header tabs so the home page works out of the box.
        if (DefaultsFactory.EnsureDefaults(Configuration))
        {
            SaveConfiguration();
        }
    }

    public static Plugin Instance { get; private set; } = null!;

    /// <summary>Gets the folder where per-user settings are stored.</summary>
    public string DataDirectory { get; }

    public override Guid Id => Guid.Parse(PluginGuid);

    public override string Name => "Homepage";

    public override string Description => "Hero, barre de navigation, sections d'accueil personnalisables et intégration Seerr.";

    /// <summary>Incremented on every configuration save so clients can drop cached layouts.</summary>
    public int ConfigurationGeneration { get; private set; }

    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        if (configuration is PluginConfiguration config)
        {
            DefaultsFactory.Normalize(config);
        }

        base.UpdateConfiguration(configuration);
        ConfigurationGeneration++;
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "Homepage",
            DisplayName = "Homepage",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.config.html",
            EnableInMainMenu = true,
            MenuIcon = "home",
        };
    }
}
