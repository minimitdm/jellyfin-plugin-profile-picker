using Jellyfin.Plugin.ProfilePicker.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ProfilePicker;

/// <summary>
/// Enregistrement des services du plugin.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<AvatarStore>();
        serviceCollection.AddSingleton<HouseholdService>();
        serviceCollection.AddSingleton<ProfileManager>();
        serviceCollection.AddSingleton<WebInjector>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<WebInjector>());
    }
}
