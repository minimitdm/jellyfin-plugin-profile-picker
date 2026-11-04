using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.ProfilePicker.Configuration;
using Jellyfin.Plugin.ProfilePicker.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ProfilePicker;

/// <summary>
/// Plugin « Profile Picker » : écran de sélection de profils façon plateformes de streaming.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>Identifiant unique du plugin (doit rester stable entre les versions).</summary>
    public const string PluginGuid = "28e96038-2eff-428b-bdb2-c2115837e144";

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Chemins applicatifs Jellyfin.</param>
    /// <param name="xmlSerializer">Sérialiseur XML.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        Paths = applicationPaths;
    }

    /// <summary>Instance courante du plugin.</summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>Chemins applicatifs exposés aux services du plugin.</summary>
    public IApplicationPaths Paths { get; }

    /// <inheritdoc />
    public override string Name => "Profile Picker";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse(PluginGuid);

    /// <inheritdoc />
    public override string Description =>
        "Écran « Qui regarde ? » sur la page de connexion, avec bibliothèque d'avatars prédéfinis.";

    /// <summary>Déclenché après l'enregistrement de la configuration.</summary>
    public event EventHandler? ConfigurationSaved;

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "ProfilePicker",
                DisplayName = "Profile Picker",
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace),
                EnableInMainMenu = true,
                MenuSection = "server",
                MenuIcon = "face"
            }
        };
    }

    /// <inheritdoc />
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        base.UpdateConfiguration(configuration);
        ConfigurationSaved?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public override void OnUninstalling()
    {
        // Retire le <script> injecté dans jellyfin-web pour laisser une install propre.
        WebInjector.Remove(Paths.WebPath, null);
        base.OnUninstalling();
    }
}
