using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.ProfilePicker.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProfilePicker.Services;

/// <summary>Erreur fonctionnelle renvoyée telle quelle au client.</summary>
public class ProfileException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileException"/> class.
    /// </summary>
    /// <param name="status">Code HTTP.</param>
    /// <param name="code">Code d'erreur.</param>
    /// <param name="message">Message lisible.</param>
    public ProfileException(int status, string code, string message)
        : base(message)
    {
        Status = status;
        Code = code;
    }

    /// <summary>Code HTTP.</summary>
    public int Status { get; }

    /// <summary>Code d'erreur.</summary>
    public string Code { get; }
}

/// <summary>
/// Création / modification / suppression de profils par les membres d'un foyer.
/// Un profil est un utilisateur Jellyfin masqué, à mot de passe aléatoire, rattaché au foyer.
/// </summary>
public class ProfileManager
{
    private static readonly Regex NameRegex = new(@"^[\w \-'.]{1,32}$", RegexOptions.Compiled);

    private readonly IUserManager _userManager;
    private readonly IProviderManager _providerManager;
    private readonly IServerConfigurationManager _serverConfig;
    private readonly AvatarStore _avatars;
    private readonly HouseholdService _households;
    private readonly ILogger<ProfileManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileManager"/> class.
    /// </summary>
    /// <param name="userManager">Gestionnaire d'utilisateurs.</param>
    /// <param name="providerManager">Gestionnaire de fournisseurs (images).</param>
    /// <param name="serverConfig">Configuration serveur.</param>
    /// <param name="avatars">Avatars.</param>
    /// <param name="households">Foyers.</param>
    /// <param name="logger">Logger.</param>
    public ProfileManager(
        IUserManager userManager,
        IProviderManager providerManager,
        IServerConfigurationManager serverConfig,
        AvatarStore avatars,
        HouseholdService households,
        ILogger<ProfileManager> logger)
    {
        _userManager = userManager;
        _providerManager = providerManager;
        _serverConfig = serverConfig;
        _avatars = avatars;
        _households = households;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance!.Configuration;

    /// <summary>Foyer de l'utilisateur, créé à la volée si l'option est active.</summary>
    /// <param name="me">Utilisateur connecté.</param>
    /// <returns>Foyer ou null.</returns>
    public Household? GetOrCreateHousehold(Guid me)
    {
        var h = _households.FindForUser(me);
        if (h is not null || !Config.AutoHousehold || Config.Mode == "public")
        {
            return h;
        }

        var user = _userManager.GetUserById(me);
        if (user is null)
        {
            return null;
        }

        h = new Household
        {
            Name = _userManager.GetUserDto(user).Name ?? "Foyer",
            AccountUserId = me.ToString("N"),
            AccountIsProfile = true
        };
        Config.Households.Add(h);
        Plugin.Instance!.SaveConfiguration();
        _logger.LogInformation("[ProfilePicker] Foyer créé automatiquement pour {User}", h.Name);
        return h;
    }

    /// <summary>L'utilisateur est-il administrateur ?</summary>
    /// <param name="userId">Utilisateur.</param>
    /// <returns>Vrai si admin.</returns>
    public bool IsAdmin(Guid userId)
    {
        var u = _userManager.GetUserById(userId);
        return u is not null && _userManager.GetUserDto(u).Policy?.IsAdministrator == true;
    }

    /// <summary>Crée un profil dans le foyer de l'appelant.</summary>
    /// <param name="me">Appelant.</param>
    /// <param name="name">Nom du profil.</param>
    /// <param name="avatarId">Avatar (facultatif).</param>
    /// <param name="pin">PIN (facultatif).</param>
    /// <returns>Id du profil.</returns>
    public async Task<Guid> CreateAsync(Guid me, string? name, string? avatarId, string? pin)
    {
        var h = RequireManageable(me);
        if (HouseholdService.SelectableIds(h).Count() >= Math.Clamp(Config.MaxProfiles, 1, 20))
        {
            throw new ProfileException(409, "limit", $"Ce foyer a déjà {Config.MaxProfiles} profils.");
        }

        var display = ValidateName(name);
        ValidatePin(pin);

        var account = _userManager.GetUserById(Guid.Parse(h.AccountUserId));
        var userName = UniqueUserName(display, account is null ? null : _userManager.GetUserDto(account).Name);

        var user = await _userManager.CreateUserAsync(userName).ConfigureAwait(false);
        var id = user.Id;
        try
        {
            await InvokeCompat("ChangePassword", user, id, Convert.ToHexString(RandomNumberGenerator.GetBytes(24))).ConfigureAwait(false);
            await _userManager.UpdatePolicyAsync(id, BuildPolicy(id, account is null ? null : _userManager.GetUserDto(account).Policy)).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(avatarId))
            {
                await SetAvatarAsync(id, avatarId).ConfigureAwait(false);
            }

            h.ProfileUserIds.Add(id.ToString("N"));
            Plugin.Instance!.SaveConfiguration();
            if (!string.IsNullOrEmpty(pin))
            {
                _households.SetPin(id, pin);
            }
        }
        catch
        {
            // Pas d'utilisateur orphelin si une étape échoue.
            await _userManager.DeleteUserAsync(id).ConfigureAwait(false);
            throw;
        }

        _logger.LogInformation("[ProfilePicker] Profil « {Name} » créé dans le foyer « {Household} »", userName, h.Name);
        return id;
    }

    /// <summary>Modifie un profil du foyer.</summary>
    /// <param name="me">Appelant.</param>
    /// <param name="target">Profil.</param>
    /// <param name="req">Modifications.</param>
    /// <returns>Tâche.</returns>
    public async Task UpdateAsync(Guid me, Guid target, ProfileEditRequest req)
    {
        var h = RequireManageable(me);
        RequireEditable(h, me, target, req.CurrentPin);
        var user = _userManager.GetUserById(target) ?? throw new ProfileException(404, "notfound", "Profil introuvable.");

        if (!string.IsNullOrWhiteSpace(req.Name))
        {
            var display = ValidateName(req.Name);
            var current = _userManager.GetUserDto(user).Name;
            if (!string.Equals(display, current, StringComparison.Ordinal))
            {
                var other = _userManager.GetUserByName(display);
                if (other is not null && other.Id != target)
                {
                    throw new ProfileException(409, "name", "Ce nom est déjà utilisé sur le serveur.");
                }

                await InvokeCompat("RenameUser", user, target, display, current!).ConfigureAwait(false);
            }
        }

        if (!string.IsNullOrEmpty(req.AvatarId))
        {
            await SetAvatarAsync(target, req.AvatarId).ConfigureAwait(false);
        }

        if (req.RemovePin == true)
        {
            if (IsAdmin(target))
            {
                throw new ProfileException(403, "adminpin", "Un profil administrateur doit garder un PIN.");
            }

            _households.SetPin(target, null);
        }
        else if (!string.IsNullOrEmpty(req.Pin))
        {
            ValidatePin(req.Pin);
            _households.SetPin(target, req.Pin);
        }
    }

    /// <summary>Supprime un profil (et l'utilisateur Jellyfin associé).</summary>
    /// <param name="me">Appelant.</param>
    /// <param name="target">Profil.</param>
    /// <param name="currentPin">PIN du profil s'il est verrouillé.</param>
    /// <returns>Tâche.</returns>
    public async Task DeleteAsync(Guid me, Guid target, string? currentPin)
    {
        var h = RequireManageable(me);
        if (HouseholdService.IsAccount(h, target))
        {
            throw new ProfileException(403, "account", "Le compte de connexion du foyer ne peut pas être supprimé ici.");
        }

        if (target == me)
        {
            throw new ProfileException(409, "self", "Passe sur un autre profil avant de supprimer celui-ci.");
        }

        if (IsAdmin(target))
        {
            throw new ProfileException(403, "admin", "Un profil administrateur ne peut pas être supprimé ici.");
        }

        RequireEditable(h, me, target, currentPin);

        h.ProfileUserIds.RemoveAll(p => Guid.TryParse(p, out var g) && g == target);
        _households.SetPin(target, null);
        Plugin.Instance!.SaveConfiguration();
        await _userManager.DeleteUserAsync(target).ConfigureAwait(false);
        _logger.LogInformation("[ProfilePicker] Profil {Profile} supprimé du foyer « {Household} »", target, h.Name);
    }

    /// <summary>Applique un avatar de la bibliothèque comme image de l'utilisateur.</summary>
    /// <param name="userId">Utilisateur.</param>
    /// <param name="avatarId">Avatar.</param>
    /// <returns>Tâche.</returns>
    public async Task SetAvatarAsync(Guid userId, string avatarId)
    {
        var user = _userManager.GetUserById(userId) ?? throw new ProfileException(404, "notfound", "Profil introuvable.");
        using var stream = _avatars.Open(avatarId, out var mime) ?? throw new ProfileException(404, "avatar", "Avatar introuvable.");

        var ext = mime switch
        {
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => ".png"
        };
        var root = _serverConfig.ApplicationPaths.UserConfigurationDirectoryPath;
        var dir = Path.GetFullPath(Path.Combine(root, user.Username));
        if (!dir.StartsWith(Path.GetFullPath(root), StringComparison.Ordinal))
        {
            throw new ProfileException(400, "path", "Nom d'utilisateur invalide.");
        }

        if (user.ProfileImage is not null)
        {
            await _userManager.ClearProfileImageAsync(user).ConfigureAwait(false);
        }

        user.ProfileImage = new Jellyfin.Database.Implementations.Entities.ImageInfo(Path.Combine(dir, "profile" + ext));
        await _providerManager.SaveImage(stream, mime, user.ProfileImage.Path).ConfigureAwait(false);
        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
    }

    // La signature de certaines méthodes d'IUserManager a changé entre versions de Jellyfin
    // (User vs Guid en 1er paramètre, ancien nom ou non) : on résout la surcharge disponible à l'exécution.
    private Task InvokeCompat(string method, object user, Guid userId, string value, string? oldName = null)
    {
        foreach (var m in _userManager.GetType().GetInterfaces().Append(_userManager.GetType())
                     .SelectMany(t => t.GetMethods())
                     .Where(m => m.Name == method))
        {
            var ps = m.GetParameters();
            object?[]? args = null;
            if (ps.Length == 2 && ps[0].ParameterType == typeof(Guid) && ps[1].ParameterType == typeof(string))
            {
                args = new object?[] { userId, value };
            }
            else if (ps.Length == 2 && ps[0].ParameterType.IsInstanceOfType(user) && ps[1].ParameterType == typeof(string))
            {
                args = new object?[] { user, value };
            }
            else if (ps.Length == 3 && ps[0].ParameterType == typeof(Guid) && ps[1].ParameterType == typeof(string) && ps[2].ParameterType == typeof(string))
            {
                args = new object?[] { userId, oldName ?? string.Empty, value };
            }
            else if (ps.Length == 3 && ps[0].ParameterType.IsInstanceOfType(user) && ps[1].ParameterType == typeof(string) && ps[2].ParameterType == typeof(string))
            {
                args = new object?[] { user, oldName ?? string.Empty, value };
            }

            if (args is not null)
            {
                var result = m.Invoke(_userManager, args);
                return result as Task ?? Task.CompletedTask;
            }
        }

        throw new ProfileException(500, "compat", $"Méthode IUserManager.{method} introuvable dans cette version de Jellyfin.");
    }

    private Household RequireManageable(Guid me)
    {
        if (!Config.AllowProfileManagement)
        {
            throw new ProfileException(403, "disabled", "La gestion des profils est désactivée par l'administrateur.");
        }

        return GetOrCreateHousehold(me) ?? throw new ProfileException(403, "nohousehold", "Ce compte n'est rattaché à aucun foyer.");
    }

    // Un profil verrouillé ne se modifie que depuis lui-même ou avec son PIN ; un admin uniquement depuis lui-même.
    private void RequireEditable(Household h, Guid me, Guid target, string? currentPin)
    {
        if (!HouseholdService.SelectableIds(h).Contains(target))
        {
            throw new ProfileException(403, "forbidden", "Ce profil n'appartient pas à ton foyer.");
        }

        if (target == me)
        {
            return;
        }

        if (IsAdmin(target))
        {
            throw new ProfileException(403, "admin", "Un profil administrateur ne se modifie que depuis ce profil.");
        }

        if (_households.HasPin(target))
        {
            if (_households.LockedFor(target) is not null)
            {
                throw new ProfileException(429, "locked", "Trop d'essais, réessaie plus tard.");
            }

            if (!_households.VerifyPin(target, currentPin))
            {
                throw new ProfileException(422, "pin", "Code PIN incorrect.");
            }
        }
    }

    private static string ValidateName(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (!NameRegex.IsMatch(n))
        {
            throw new ProfileException(400, "name", "Nom invalide (1 à 32 caractères : lettres, chiffres, espace, - ' .).");
        }

        return n;
    }

    private static void ValidatePin(string? pin)
    {
        if (string.IsNullOrEmpty(pin))
        {
            return;
        }

        var len = Math.Clamp(Config.PinLength, 4, 8);
        if (pin.Length != len || !pin.All(char.IsAsciiDigit))
        {
            throw new ProfileException(400, "pinformat", $"Le PIN doit contenir {len} chiffres.");
        }
    }

    // Les noms d'utilisateur Jellyfin sont uniques sur tout le serveur.
    private string UniqueUserName(string display, string? accountName)
    {
        if (_userManager.GetUserByName(display) is null)
        {
            return display;
        }

        var baseName = string.IsNullOrEmpty(accountName) ? display : display + "-" + accountName;
        if (baseName.Length > 48)
        {
            baseName = baseName[..48];
        }

        var candidate = baseName;
        for (var i = 2; _userManager.GetUserByName(candidate) is not null; i++)
        {
            candidate = baseName + "-" + i.ToString(CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    // Politique du profil : celle du compte foyer (bibliothèques, contrôle parental…), masqué, jamais admin.
    // Si le compte foyer n'a accès à aucune médiathèque (compte « portail »), le profil reçoit toutes les médiathèques.
    private UserPolicy BuildPolicy(Guid newUserId, UserPolicy? accountPolicy)
    {
        var newUser = _userManager.GetUserById(newUserId)!;
        var basePolicy = _userManager.GetUserDto(newUser).Policy ?? new UserPolicy();
        UserPolicy policy = basePolicy;

        if (accountPolicy is not null)
        {
            policy = JsonSerializer.Deserialize<UserPolicy>(JsonSerializer.Serialize(accountPolicy)) ?? basePolicy;
            policy.AuthenticationProviderId = basePolicy.AuthenticationProviderId;
            policy.PasswordResetProviderId = basePolicy.PasswordResetProviderId;
            if (!policy.EnableAllFolders && (policy.EnabledFolders is null || policy.EnabledFolders.Length == 0))
            {
                policy.EnableAllFolders = true;
            }
        }

        policy.IsAdministrator = false;
        policy.IsHidden = true;
        policy.IsDisabled = false;
        return policy;
    }
}

/// <summary>Requête de création / modification de profil.</summary>
public class ProfileEditRequest
{
    /// <summary>Nom.</summary>
    public string? Name { get; set; }

    /// <summary>Avatar de la bibliothèque.</summary>
    public string? AvatarId { get; set; }

    /// <summary>Nouveau PIN.</summary>
    public string? Pin { get; set; }

    /// <summary>Retirer le PIN.</summary>
    public bool? RemovePin { get; set; }

    /// <summary>PIN actuel du profil ciblé (s'il est verrouillé et que ce n'est pas le profil courant).</summary>
    public string? CurrentPin { get; set; }
}
