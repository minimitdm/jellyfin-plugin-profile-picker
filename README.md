# Jellyfin Profile Picker

Écran **« Qui regarde ? »** façon plateformes de streaming pour jellyfin-web, avec une **bibliothèque d'avatars prédéfinis** (33 intégrés + les tiens).

Deux modes :

- **Foyer** (défaut) : on se connecte avec **un compte + mot de passe**, puis on choisit son **profil** (chacun avec son historique, ses favoris, son contrôle parental et un PIN optionnel). Changement de profil depuis le menu utilisateur.
- **Public** : la grille des utilisateurs s'affiche directement sur la page de connexion.

![foyer](docs/foyer-picker.png)

| Thème « Cinéma » | Thème « Bleu nuit » |
|---|---|
| ![cinema](docs/theme-cinema.png) | ![nightblue](docs/theme-nightblue.png) |

| PIN | Sélecteur d'avatars |
|---|---|
| ![pin](docs/pin.png) | ![avatars](docs/avatars.png) |

## Mode foyer — profils créés par les utilisateurs

Par défaut (*Créer automatiquement un foyer*), **n'importe quel compte qui se connecte devient un foyer** dont il est le premier profil. Depuis l'écran « Qui regarde ? » :

- **Ajouter un profil** : nom, avatar, PIN facultatif. Le plugin crée côté serveur un utilisateur Jellyfin masqué (mot de passe aléatoire), avec les mêmes médiathèques et le même contrôle parental que le compte. Chaque profil a donc son historique, ses favoris et son « Reprendre » à lui.
- **Gérer les profils** : renommer, changer d'avatar, définir/retirer un PIN, supprimer (le PIN du profil est demandé s'il est verrouillé).
- Un profil **administrateur** ne peut être ouvert depuis un autre profil que s'il a un PIN, et ne se modifie que depuis lui-même.

![gérer](docs/gerer-profils.png)

## Mode foyer — mise en place manuelle (admin)

1. **Créer le compte de connexion** (Tableau de bord → Utilisateurs) : ex. `famille-martin` avec un mot de passe fort.
   Recommandé : aucun accès aux médiathèques, case *Masquer cet utilisateur des écrans de connexion* cochée.
2. **Tableau de bord → Profile Picker → Foyers → Ajouter un foyer** : nom, compte de connexion.
3. **Ajouter les profils** : coche des utilisateurs existants ou clique **Nouveau profil…** (crée un utilisateur Jellyfin masqué avec un mot de passe aléatoire de 48 caractères : personne ne s'y connecte directement).
4. Optionnel : **Définir PIN** sur les profils sensibles (parents, admin), **Avatar** pour chaque profil, contrôle parental dans la fiche utilisateur Jellyfin.
5. Enregistrer, puis Ctrl+F5.

Côté utilisateur : `http://IP:8096` → formulaire Jellyfin (compte du foyer) → **Qui regarde ?** → accueil du profil choisi.
Menu avatar (en haut à droite) → **Changer de profil**. Avec *Redemander à chaque ouverture*, l'écran revient à chaque nouvel onglet / relance de l'app.

**Ce qui se passe côté serveur** : le choix d'un profil appelle `POST /ProfilePicker/Profiles/{id}/Switch`. Le plugin vérifie que l'appelant (compte foyer ou profil) et la cible sont dans le même foyer, contrôle le PIN (PBKDF2-SHA256, 5 échecs → blocage 5 min), ouvre une session pour le profil via `ISessionManager.AuthenticateDirect` (même mécanisme que Quick Connect) sur le même DeviceId, puis **révoque le jeton précédent**. Une seule session active par appareil.

## Fonctionnalités

- Grille de profils plein écran à la place de la page de login (utilisateurs « visibles sur l'écran de connexion »).
- Clic sur un profil sans mot de passe → connexion directe. Profil protégé → fenêtre mot de passe **ou code PIN** (4 à 8 chiffres).
- L'authentification passe par **le formulaire natif** de jellyfin-web : le plugin ne voit et ne stocke aucun mot de passe.
- 2 thèmes (fond noir / avatars carrés, dégradé bleu nuit / avatars ronds), titre et couleur d'accent configurables.
- Boutons « Autre utilisateur » (formulaire classique) et Quick Connect.
- **Avatars prédéfinis** : 33 avatars originaux (classiques, animaux, robots, espace) embarqués dans la DLL.
- **Avatars personnalisés** : upload depuis le tableau de bord (PNG/JPEG/WEBP/GIF, 3 Mo max).
- Page d'admin : assigner un avatar à chaque utilisateur, masquer/afficher un profil de l'écran, état de l'injection.
- Bouton « Avatar prédéfini » sur la page Profil de chaque utilisateur (désactivable).

## Compatibilité

| Jellyfin | .NET | Paquet publié |
|---|---|---|
| 12.x | net10.0 | `X.Y.Z.1` (targetAbi 12.0.0.0) |
| 10.11.x | net9.0 | `X.Y.Z.0` (targetAbi 10.11.0.0) |

Le serveur choisit automatiquement le bon build depuis le dépôt.
Côté clients : jellyfin-web (navigateur, Jellyfin Media Player, apps Android/iOS qui embarquent le web). Les clients natifs (Android TV, Swiftfin, Infuse…) ont leur propre écran de connexion et ne sont pas concernés.

## Installation

### Via le dépôt de plugins (recommandé)

1. Tableau de bord → **Plugins** → **Dépôts** → **+**
   - Nom : `Profile Picker`
   - URL : `https://raw.githubusercontent.com/minimitdm/jellyfin-plugin-profile-picker/main/manifest.json`
2. **Catalogue** → *Profile Picker* → Installer.
3. Redémarrer Jellyfin, puis **Ctrl+F5** dans le navigateur.

### Dépôt auto-hébergé (sans GitHub)

Sur un PC Windows avec le SDK .NET 10 (`winget install Microsoft.DotNet.SDK.10`) :

```powershell
.\scripts\build-local.ps1 -Version 1.0.0 -BaseUrl http://<IP_NAS>/profile-picker
```

Copier le contenu de `repo\` dans le dossier web correspondant (ex. Synology Web Station : `web\profile-picker\`), puis ajouter `http://<IP_NAS>/profile-picker/manifest.json` dans *Plugins → Dépôts*.
Nouvelle version : relancer le script avec `-Version 1.0.1` et recopier `repo\` (les anciennes versions sont conservées dans le manifest).

### Manuelle

Décompresser le zip de la [release](../../releases) dans `<config>/plugins/ProfilePicker_X.Y.Z.N/` puis redémarrer.
Sur Synology (paquet SynoCommunity) le dossier `plugins` est dans le répertoire de données du paquet (visible dans *Tableau de bord → Paramètres → Chemins*).

## Comment le script est chargé

jellyfin-web n'a pas d'API d'extension côté client : au démarrage, le plugin ajoute dans `index.html` de jellyfin-web un bloc balisé :

```html
<!-- ProfilePicker:start --><link rel="stylesheet" href="../ProfilePicker/Client/profile-picker.css?v=…"><script defer src="../ProfilePicker/Client/profile-picker.js?v=…"></script><!-- ProfilePicker:end -->
```

- Le bloc est **ré-appliqué à chaque démarrage** (les mises à jour Jellyfin écrasent `index.html`) et **retiré à la désinstallation** ou si tu désactives le plugin.
- Chemins relatifs → compatible avec une *BaseUrl* (`/jellyfin`) et un reverse proxy.
- Cohabite avec d'autres plugins qui injectent du JS (Jellyfin-Enhanced, etc.) : seul le bloc balisé est modifié.

**Si l'état affiche « index.html non modifiable »** (le compte du service n'a pas les droits sur le dossier web, conteneur en lecture seule…), deux options :

1. Donner l'écriture au compte du service sur `index.html` (chemin affiché dans la page du plugin), ex. sur Linux : `chown jellyfin: /usr/share/jellyfin/web/index.html`.
2. Ou décocher *Injection automatique* et utiliser le plugin **JavaScript Injector** avec :

```js
const css = document.createElement('link'); css.rel = 'stylesheet'; css.href = '../ProfilePicker/Client/profile-picker.css'; document.head.appendChild(css);
const js = document.createElement('script'); js.src = '../ProfilePicker/Client/profile-picker.js'; document.head.appendChild(js);
```

## API

| Méthode | Route | Accès |
|---|---|---|
| GET | `/ProfilePicker/Client/{profile-picker.js\|.css}` | anonyme |
| GET | `/ProfilePicker/Config` | anonyme (config d'affichage uniquement) |
| GET | `/ProfilePicker/Avatars` · `/ProfilePicker/Avatars/{id}` | anonyme |
| POST | `/ProfilePicker/Avatars?name=…` (corps = image brute) | admin |
| DELETE | `/ProfilePicker/Avatars/{id}` | admin |
| GET | `/ProfilePicker/Profiles` | utilisateur connecté (profils de son foyer) |
| POST | `/ProfilePicker/Profiles/{id}/Switch` `{"Pin":"1234"}` | utilisateur connecté, même foyer |
| POST | `/ProfilePicker/Profiles` `{"Name","AvatarId","Pin"}` | membre du foyer (crée un profil) |
| POST | `/ProfilePicker/Profiles/{id}/Update` `{"Name","AvatarId","Pin","RemovePin","CurrentPin"}` | membre du foyer |
| POST | `/ProfilePicker/Profiles/{id}/Delete` `{"CurrentPin"}` | membre du foyer |
| POST | `/ProfilePicker/Profiles/{id}/Pin` `{"Pin":"…"}` (vide = suppression) | admin |
| GET | `/ProfilePicker/Status` · POST `/ProfilePicker/Inject` | admin |

L'application d'un avatar à un utilisateur passe par l'API standard Jellyfin (`POST /UserImage?userId=…`) avec les droits de la session : un utilisateur ne peut changer que le sien, un admin ceux de tout le monde.

## Sécurité — à lire si le serveur est exposé sur Internet

**Mode foyer** (recommandé pour un accès distant) :
- Seul le compte du foyer s'authentifie par mot de passe ; masque-le de l'écran de connexion pour que `/Users/Public` ne révèle aucun nom.
- Tout membre d'un foyer peut basculer sur n'importe quel profil de ce foyer (comme Netflix) : **mets un PIN sur les profils parents/admin**, ou mieux, ne mets aucun compte admin dans un foyer.
- Le compte foyer ne doit pas avoir accès aux médiathèques : l'écran de sélection forcé est côté client, l'accès réel est contrôlé par les droits Jellyfin.

**Mode public** :

- L'écran utilise `GET /Users/Public`, la même route que la page de login native : **les noms des profils visibles sont publics**. Masque les comptes sensibles (admin) via *Masquer cet utilisateur des écrans de connexion* (case « Visible sur l'écran » dans la page du plugin).
- Un PIN 4 chiffres = 10 000 combinaisons. Garde le verrouillage Jellyfin (*Nombre max. de tentatives de connexion échouées*) actif et réserve le mode PIN aux comptes non-admin ; les comptes admin doivent garder un vrai mot de passe et rester masqués.
- Derrière un reverse proxy, ajoute du rate-limiting / fail2ban sur `/Users/AuthenticateByName`.

## Développement

```bash
# build Jellyfin 12 (défaut) ou 10.11
dotnet build src/Jellyfin.Plugin.ProfilePicker -c Release
dotnet build src/Jellyfin.Plugin.ProfilePicker -c Release -p:JellyfinTarget=10.11

# régénérer les avatars intégrés (Pillow)
python3 tools/generate_avatars.py
```

Ajouter des avatars intégrés : déposer des PNG carrés dans `src/Jellyfin.Plugin.ProfilePicker/Avatars/<categorie>/<nom>.png` (noms en minuscules, sans espace ni point).

### Publier une version

Mettre le numéro dans le fichier `VERSION` (ex. `1.0.1`) et pousser sur `main` — ou lancer *Actions → Release → Run workflow*.
Le workflow *Release* compile les deux cibles, crée la release GitHub avec les zips et met à jour `manifest.json` sur `main` (checksums MD5 inclus).

## Licence

GPL-3.0 (comme Jellyfin). Les avatars intégrés sont des créations originales générées par `tools/generate_avatars.py`, distribuées sous la même licence. Projet non affilié à Jellyfin ni à une quelconque plateforme de streaming.
