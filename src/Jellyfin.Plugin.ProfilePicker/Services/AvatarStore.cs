using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProfilePicker.Services;

/// <summary>Un avatar disponible (intégré ou ajouté par l'admin).</summary>
/// <param name="Id">Identifiant stable.</param>
/// <param name="Name">Nom affiché.</param>
/// <param name="Category">Catégorie.</param>
/// <param name="Custom">Vrai si ajouté par l'administrateur.</param>
public sealed record AvatarInfo(string Id, string Name, string Category, bool Custom);

/// <summary>
/// Bibliothèque d'avatars : PNG embarqués dans la DLL + dossier personnalisé
/// <c>{config}/plugins/configurations/ProfilePicker/avatars</c>.
/// </summary>
public class AvatarStore
{
    /// <summary>Taille maximale d'un avatar personnalisé (octets).</summary>
    public const int MaxUploadBytes = 3 * 1024 * 1024;

    private const string BuiltinPrefix = "builtin.";
    private const string CustomPrefix = "custom.";
    private static readonly string ResourcePrefix = typeof(AvatarStore).Namespace!.Replace(".Services", string.Empty, StringComparison.Ordinal) + ".Avatars.";

    private static readonly Dictionary<string, string> MimeByExt = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif"
    };

    private readonly ILogger<AvatarStore> _logger;
    private readonly Assembly _assembly = typeof(AvatarStore).Assembly;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvatarStore"/> class.
    /// </summary>
    /// <param name="paths">Chemins applicatifs.</param>
    /// <param name="logger">Logger.</param>
    public AvatarStore(IApplicationPaths paths, ILogger<AvatarStore> logger)
    {
        _logger = logger;
        CustomDirectory = Path.Combine(paths.PluginConfigurationsPath, "ProfilePicker", "avatars");
        try
        {
            Directory.CreateDirectory(CustomDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[ProfilePicker] Impossible de créer {Dir}", CustomDirectory);
        }
    }

    /// <summary>Dossier des avatars personnalisés.</summary>
    public string CustomDirectory { get; }

    /// <summary>Liste tous les avatars disponibles.</summary>
    /// <returns>Avatars triés par catégorie puis nom.</returns>
    public IReadOnlyList<AvatarInfo> List()
    {
        var list = new List<AvatarInfo>();

        foreach (var res in _assembly.GetManifestResourceNames())
        {
            if (!res.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !res.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Jellyfin.Plugin.ProfilePicker.Avatars.<categorie>.<nom>.png
            var parts = res[ResourcePrefix.Length..^4].Split('.', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            list.Add(new AvatarInfo(BuiltinPrefix + parts[0] + "." + parts[1], Prettify(parts[1]), Prettify(parts[0]), false));
        }

        foreach (var file in EnumerateCustomFiles())
        {
            var fileName = Path.GetFileName(file);
            list.Add(new AvatarInfo(CustomPrefix + fileName, Prettify(Path.GetFileNameWithoutExtension(fileName)), "Personnalisés", true));
        }

        return list
            .OrderBy(a => a.Custom ? 0 : 1)
            .ThenBy(a => a.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Ouvre le flux d'un avatar.</summary>
    /// <param name="id">Identifiant.</param>
    /// <param name="mimeType">Type MIME.</param>
    /// <returns>Flux ou null si introuvable.</returns>
    public Stream? Open(string id, out string mimeType)
    {
        mimeType = "image/png";
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        if (id.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
        {
            var res = ResourcePrefix + id[BuiltinPrefix.Length..] + ".png";

            // On ne résout que des ressources réellement présentes (pas de concaténation libre).
            return _assembly.GetManifestResourceNames().Contains(res, StringComparer.Ordinal)
                ? _assembly.GetManifestResourceStream(res)
                : null;
        }

        if (id.StartsWith(CustomPrefix, StringComparison.Ordinal))
        {
            var file = FindCustom(id[CustomPrefix.Length..]);
            if (file is null)
            {
                return null;
            }

            mimeType = MimeByExt[Path.GetExtension(file)];
            return File.OpenRead(file);
        }

        return null;
    }

    /// <summary>Enregistre un avatar personnalisé.</summary>
    /// <param name="name">Nom souhaité.</param>
    /// <param name="data">Contenu de l'image.</param>
    /// <returns>Avatar créé.</returns>
    public AvatarInfo SaveCustom(string name, byte[] data)
    {
        var ext = DetectExtension(data) ?? throw new InvalidDataException("Format d'image non supporté (PNG, JPEG, WEBP ou GIF).");
        var safe = Slugify(name);
        if (safe.Length == 0)
        {
            safe = "avatar-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        }

        var fileName = safe + ext;
        var i = 2;
        while (File.Exists(Path.Combine(CustomDirectory, fileName)))
        {
            fileName = safe + "-" + i++.ToString(CultureInfo.InvariantCulture) + ext;
        }

        Directory.CreateDirectory(CustomDirectory);
        File.WriteAllBytes(Path.Combine(CustomDirectory, fileName), data);
        _logger.LogInformation("[ProfilePicker] Avatar personnalisé ajouté : {File}", fileName);
        return new AvatarInfo(CustomPrefix + fileName, Prettify(Path.GetFileNameWithoutExtension(fileName)), "Personnalisés", true);
    }

    /// <summary>Supprime un avatar personnalisé.</summary>
    /// <param name="id">Identifiant.</param>
    /// <returns>Vrai si supprimé.</returns>
    public bool DeleteCustom(string id)
    {
        if (!id.StartsWith(CustomPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var file = FindCustom(id[CustomPrefix.Length..]);
        if (file is null)
        {
            return false;
        }

        File.Delete(file);
        return true;
    }

    private IEnumerable<string> EnumerateCustomFiles()
    {
        if (!Directory.Exists(CustomDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(CustomDirectory)
            .Where(f => MimeByExt.ContainsKey(Path.GetExtension(f)));
    }

    // Recherche par énumération : aucun chemin n'est construit à partir de l'entrée utilisateur.
    private string? FindCustom(string fileName)
        => EnumerateCustomFiles().FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.Ordinal));

    private static string? DetectExtension(byte[] d)
    {
        if (d.Length > 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)
        {
            return ".png";
        }

        if (d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF)
        {
            return ".jpg";
        }

        if (d.Length > 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P')
        {
            return ".webp";
        }

        if (d.Length > 6 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F' && d[3] == '8')
        {
            return ".gif";
        }

        return null;
    }

    private static string Slugify(string input)
    {
        var sb = new StringBuilder();
        foreach (var c in (input ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c) && c < 128)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if ((c == ' ' || c == '-' || c == '_') && sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }

            if (sb.Length >= 48)
            {
                break;
            }
        }

        return sb.ToString().Trim('-');
    }

    private static string Prettify(string slug)
    {
        var s = slug.Replace('-', ' ').Replace('_', ' ');
        s = s switch
        {
            "fantome" => "fantôme",
            "fusee" => "fusée",
            "planete" => "planète",
            _ => s
        };
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }
}
