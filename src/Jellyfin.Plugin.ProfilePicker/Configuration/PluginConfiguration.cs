using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ProfilePicker.Configuration;

/// <summary>
/// Configuration du plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Active le plugin côté client.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// « household » : connexion avec un compte foyer puis choix du profil (défaut).
    /// « public »    : grille des utilisateurs publics directement sur la page de connexion.
    /// </summary>
    public string Mode { get; set; } = "household";

    /// <summary>Mode foyer : redemander le profil à chaque nouvelle ouverture de l'application (onglet).</summary>
    public bool AlwaysAskProfile { get; set; } = true;

    /// <summary>Foyers (compte de connexion + profils rattachés).</summary>
    public List<Household> Households { get; set; } = new();

    /// <summary>Codes PIN de profil (hachés PBKDF2).</summary>
    public List<ProfileLock> ProfileLocks { get; set; } = new();

    /// <summary>Thème : « cinema » (fond noir, avatars carrés) ou « nightblue » (dégradé bleu nuit, avatars ronds).</summary>
    public string Theme { get; set; } = "cinema";

    /// <summary>Titre affiché au-dessus des profils.</summary>
    public string Title { get; set; } = "Qui regarde ?";

    /// <summary>Couleur d'accent (hex).</summary>
    public string AccentColor { get; set; } = "#e50914";

    /// <summary>Mode public : saisie du mot de passe Jellyfin façon code PIN.</summary>
    public bool PinMode { get; set; }

    /// <summary>Longueur des codes PIN (4 à 8).</summary>
    public int PinLength { get; set; } = 4;

    /// <summary>Mode public : bouton « Autre utilisateur ».</summary>
    public bool ShowManualLogin { get; set; } = true;

    /// <summary>Mode public : bouton Quick Connect.</summary>
    public bool ShowQuickConnect { get; set; } = true;

    /// <summary>Mode public : état forcé de « Se souvenir de moi ».</summary>
    public bool RememberMe { get; set; } = true;

    /// <summary>Permet aux utilisateurs de choisir eux-mêmes un avatar depuis leur page Profil.</summary>
    public bool AllowUserAvatarChoice { get; set; } = true;

    /// <summary>Injecte automatiquement le script dans l'index.html de jellyfin-web.</summary>
    public bool AutoInject { get; set; } = true;
}

/// <summary>Un foyer : un compte de connexion et ses profils.</summary>
public class Household
{
    /// <summary>Nom du foyer (affiché sur l'écran de sélection).</summary>
    public string Name { get; set; } = "Foyer";

    /// <summary>Id (Guid) de l'utilisateur Jellyfin servant de compte de connexion.</summary>
    public string AccountUserId { get; set; } = string.Empty;

    /// <summary>Le compte de connexion apparaît aussi comme profil.</summary>
    public bool AccountIsProfile { get; set; }

    /// <summary>Ids (Guid) des utilisateurs Jellyfin servant de profils.</summary>
    public List<string> ProfileUserIds { get; set; } = new();
}

/// <summary>Code PIN d'un profil.</summary>
public class ProfileLock
{
    /// <summary>Id (Guid) de l'utilisateur.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Sel (base64).</summary>
    public string Salt { get; set; } = string.Empty;

    /// <summary>Empreinte PBKDF2-SHA256 (base64).</summary>
    public string Hash { get; set; } = string.Empty;
}
