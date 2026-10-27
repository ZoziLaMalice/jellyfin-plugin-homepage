using Jellyfin.Plugin.Homepage.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Homepage;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<UserSettingsStore>();
        serviceCollection.AddSingleton<LibraryService>();
        serviceCollection.AddSingleton<SeerrService>();
        serviceCollection.AddSingleton<SonarrService>();
        serviceCollection.AddSingleton<LayoutService>();
    }
}
