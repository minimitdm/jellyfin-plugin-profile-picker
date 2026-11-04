using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ProfilePicker.Configuration;

/// <summary>
/// Configuration du plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Active l'écran de sélection de profils sur la page de connexion.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Thème : « cinema » (fond noir, avatars carrés) ou « nightblue » (dégradé bleu nuit, avatars ronds).</summary>
    public string Theme { get; set; } = "cinema";

    /// <summary>Titre affiché au-dessus des profils.</summary>
    public string Title { get; set; } = "Qui regarde ?";

    /// <summary>Couleur d'accent (hex).</summary>
    public string AccentColor { get; set; } = "#e50914";

    /// <summary>Saisie du mot de passe en mode code PIN (4 à 8 chiffres).</summary>
    public bool PinMode { get; set; }

    /// <summary>Longueur du code PIN quand <see cref="PinMode"/> est actif.</summary>
    public int PinLength { get; set; } = 4;

    /// <summary>Affiche le bouton « Autre utilisateur » (formulaire natif).</summary>
    public bool ShowManualLogin { get; set; } = true;

    /// <summary>Affiche le bouton Quick Connect si le serveur l'autorise.</summary>
    public bool ShowQuickConnect { get; set; } = true;

    /// <summary>Force l'état de la case « Se souvenir de moi » lors d'une connexion via un profil.</summary>
    public bool RememberMe { get; set; } = true;

    /// <summary>Permet aux utilisateurs de choisir eux-mêmes un avatar depuis leur page Profil.</summary>
    public bool AllowUserAvatarChoice { get; set; } = true;

    /// <summary>Injecte automatiquement le script dans l'index.html de jellyfin-web.</summary>
    public bool AutoInject { get; set; } = true;
}
