using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProfilePicker.Services;

/// <summary>
/// Ajoute (ou retire) la balise &lt;script&gt; du plugin dans l'index.html de jellyfin-web.
/// Exécuté au démarrage du serveur et à chaque enregistrement de la configuration.
/// </summary>
public class WebInjector : IHostedService
{
    private const string StartMarker = "<!-- ProfilePicker:start -->";
    private const string EndMarker = "<!-- ProfilePicker:end -->";
    private static readonly Regex BlockRegex = new(
        Regex.Escape(StartMarker) + ".*?" + Regex.Escape(EndMarker) + @"\s*",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly IApplicationPaths _paths;
    private readonly ILogger<WebInjector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebInjector"/> class.
    /// </summary>
    /// <param name="paths">Chemins applicatifs.</param>
    /// <param name="logger">Logger.</param>
    public WebInjector(IApplicationPaths paths, ILogger<WebInjector> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <summary>Dernier état connu de l'injection (affiché dans la page de config).</summary>
    public static InjectionStatus LastStatus { get; private set; } = new(false, null, "Pas encore exécuté");

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationSaved += (_, _) => Apply();
        }

        Apply();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Applique l'état voulu selon la configuration.</summary>
    /// <returns>Statut.</returns>
    public InjectionStatus Apply()
    {
        var cfg = Plugin.Instance?.Configuration;
        if (cfg is null || !cfg.Enabled || !cfg.AutoInject)
        {
            return LastStatus = Remove(_paths.WebPath, _logger);
        }

        var version = typeof(WebInjector).Assembly.GetName().Version?.ToString() ?? "0";
        return LastStatus = Inject(_paths.WebPath, version, _logger);
    }

    /// <summary>Injecte la balise script.</summary>
    /// <param name="webPath">Dossier jellyfin-web.</param>
    /// <param name="version">Version (cache-busting).</param>
    /// <param name="logger">Logger.</param>
    /// <returns>Statut.</returns>
    public static InjectionStatus Inject(string webPath, string version, ILogger? logger)
    {
        var index = Path.Combine(webPath ?? string.Empty, "index.html");
        try
        {
            if (!File.Exists(index))
            {
                return Fail(index, "index.html introuvable (jellyfin-web hébergé à part ?)", null, logger);
            }

            var html = File.ReadAllText(index);
            var cleaned = BlockRegex.Replace(html, string.Empty);

            // Chemin relatif à /web/ : compatible avec une BaseUrl (/jellyfin/web/ -> /jellyfin/ProfilePicker/...)
            var block = StartMarker
                + "<link rel=\"stylesheet\" href=\"../ProfilePicker/Client/profile-picker.css?v=" + version + "\">"
                + "<script defer src=\"../ProfilePicker/Client/profile-picker.js?v=" + version + "\"></script>"
                + EndMarker;

            var pos = cleaned.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (pos < 0)
            {
                return Fail(index, "balise </body> absente dans index.html", null, logger);
            }

            var patched = cleaned.Insert(pos, block + "\n");
            if (!string.Equals(patched, html, StringComparison.Ordinal))
            {
                File.WriteAllText(index, patched);
                logger?.LogInformation("[ProfilePicker] Script injecté dans {Index}", index);
            }

            return new InjectionStatus(true, index, "Script injecté");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(index, "index.html non modifiable par le compte du service Jellyfin", ex, logger);
        }
    }

    /// <summary>Retire la balise script.</summary>
    /// <param name="webPath">Dossier jellyfin-web.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>Statut.</returns>
    public static InjectionStatus Remove(string webPath, ILogger? logger)
    {
        var index = Path.Combine(webPath ?? string.Empty, "index.html");
        try
        {
            if (!File.Exists(index))
            {
                return new InjectionStatus(false, index, "index.html introuvable");
            }

            var html = File.ReadAllText(index);
            var cleaned = BlockRegex.Replace(html, string.Empty);
            if (!string.Equals(cleaned, html, StringComparison.Ordinal))
            {
                File.WriteAllText(index, cleaned);
                logger?.LogInformation("[ProfilePicker] Script retiré de {Index}", index);
            }

            return new InjectionStatus(false, index, "Injection désactivée");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(index, "index.html non modifiable", ex, logger);
        }
    }

    private static InjectionStatus Fail(string index, string reason, Exception? ex, ILogger? logger)
    {
        logger?.LogWarning(ex, "[ProfilePicker] {Reason} ({Index}). Utilise l'injection manuelle (voir README).", reason, index);
        return new InjectionStatus(false, index, reason);
    }
}

/// <summary>État de l'injection.</summary>
/// <param name="Injected">Script présent.</param>
/// <param name="IndexPath">Chemin de index.html.</param>
/// <param name="Message">Message lisible.</param>
public sealed record InjectionStatus(bool Injected, string? IndexPath, string Message);
