using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Security;
using System.Security.Authentication;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.ProfilePicker.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProfilePicker.Api;

/// <summary>
/// API du plugin : script client, configuration publique, bibliothèque d'avatars.
/// </summary>
[ApiController]
[Route("ProfilePicker")]
public class ProfilePickerController : ControllerBase
{
    private const string Elevated = "RequiresElevation";
    private static readonly string[] ClientFiles = { "profile-picker.js", "profile-picker.css" };
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AvatarStore _avatars;
    private readonly WebInjector _injector;
    private readonly HouseholdService _households;
    private readonly IUserManager _userManager;
    private readonly ISessionManager _sessionManager;
    private readonly IAuthorizationContext _authContext;
    private readonly ILogger<ProfilePickerController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfilePickerController"/> class.
    /// </summary>
    /// <param name="avatars">Bibliothèque d'avatars.</param>
    /// <param name="injector">Injecteur web.</param>
    /// <param name="households">Service des foyers.</param>
    /// <param name="userManager">Gestionnaire d'utilisateurs.</param>
    /// <param name="sessionManager">Gestionnaire de sessions.</param>
    /// <param name="authContext">Contexte d'autorisation.</param>
    /// <param name="logger">Logger.</param>
    public ProfilePickerController(
        AvatarStore avatars,
        WebInjector injector,
        HouseholdService households,
        IUserManager userManager,
        ISessionManager sessionManager,
        IAuthorizationContext authContext,
        ILogger<ProfilePickerController> logger)
    {
        _avatars = avatars;
        _injector = injector;
        _households = households;
        _userManager = userManager;
        _sessionManager = sessionManager;
        _authContext = authContext;
        _logger = logger;
    }

    /// <summary>Sert le script / la feuille de style client.</summary>
    /// <param name="file">Nom du fichier.</param>
    /// <returns>Contenu.</returns>
    [HttpGet("Client/{file}")]
    [AllowAnonymous]
    public ActionResult GetClientFile([FromRoute] string file)
    {
        var name = ClientFiles.FirstOrDefault(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return NotFound();
        }

        var asm = typeof(ProfilePickerController).Assembly;
        var stream = asm.GetManifestResourceStream("Jellyfin.Plugin.ProfilePicker.Web." + name);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-cache";
        var mime = name.EndsWith(".js", StringComparison.Ordinal) ? "application/javascript; charset=utf-8" : "text/css; charset=utf-8";
        return File(stream, mime);
    }

    /// <summary>Configuration publique utilisée par la page de connexion.</summary>
    /// <returns>JSON.</returns>
    [HttpGet("Config")]
    [AllowAnonymous]
    public ActionResult GetPublicConfig()
    {
        var c = Plugin.Instance!.Configuration;
        return ToJson(new
        {
            c.Enabled,
            Mode = c.Mode == "public" ? "public" : "household",
            c.AlwaysAskProfile,
            c.Theme,
            c.Title,
            c.AccentColor,
            c.PinMode,
            PinLength = Math.Clamp(c.PinLength, 4, 8),
            c.ShowManualLogin,
            c.ShowQuickConnect,
            c.RememberMe,
            c.AllowUserAvatarChoice
        });
    }

    /// <summary>Liste les avatars disponibles.</summary>
    /// <returns>JSON.</returns>
    [HttpGet("Avatars")]
    [AllowAnonymous]
    public ActionResult ListAvatars() => ToJson(_avatars.List());

    /// <summary>Renvoie l'image d'un avatar.</summary>
    /// <param name="id">Identifiant.</param>
    /// <returns>Image.</returns>
    [HttpGet("Avatars/{id}")]
    [AllowAnonymous]
    public ActionResult GetAvatar([FromRoute] string id)
    {
        var stream = _avatars.Open(id, out var mime);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=86400";
        return File(stream, mime);
    }

    /// <summary>Ajoute un avatar personnalisé (corps = image brute).</summary>
    /// <param name="name">Nom de l'avatar.</param>
    /// <returns>Avatar créé.</returns>
    [HttpPost("Avatars")]
    [Authorize(Policy = Elevated)]
    [RequestSizeLimit(AvatarStore.MaxUploadBytes + 1024)]
    public async Task<ActionResult> UploadAvatar([FromQuery] string? name)
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms).ConfigureAwait(false);
        if (ms.Length == 0 || ms.Length > AvatarStore.MaxUploadBytes)
        {
            return BadRequest("Image vide ou supérieure à 3 Mo.");
        }

        try
        {
            return ToJson(_avatars.SaveCustom(name ?? string.Empty, ms.ToArray()));
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Supprime un avatar personnalisé.</summary>
    /// <param name="id">Identifiant.</param>
    /// <returns>204 ou 404.</returns>
    [HttpDelete("Avatars/{id}")]
    [Authorize(Policy = Elevated)]
    public ActionResult DeleteAvatar([FromRoute] string id)
        => _avatars.DeleteCustom(id) ? NoContent() : NotFound();

    /// <summary>Profils du foyer de l'utilisateur connecté (compte foyer ou profil).</summary>
    /// <returns>JSON.</returns>
    [HttpGet("Profiles")]
    [Authorize]
    public async Task<ActionResult> GetProfiles()
    {
        var auth = await _authContext.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        var me = auth.UserId;
        if (me == Guid.Empty)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var h = _households.FindForUser(me);
        if (h is null)
        {
            return ToJson(new { Household = (string?)null, Profiles = Array.Empty<object>() });
        }

        var selectable = HouseholdService.SelectableIds(h).ToList();
        var profiles = new List<object>();
        foreach (var id in selectable)
        {
            var user = _userManager.GetUserById(id);
            if (user is null)
            {
                continue;
            }

            var dto = _userManager.GetUserDto(user);
            if (dto.Policy?.IsDisabled == true)
            {
                continue;
            }

            profiles.Add(new
            {
                Id = id.ToString("N"),
                dto.Name,
                ImageTag = dto.PrimaryImageTag,
                HasPin = _households.HasPin(id),
                IsCurrent = id == me
            });
        }

        var c = Plugin.Instance!.Configuration;
        return ToJson(new
        {
            Household = h.Name,
            IsGate = HouseholdService.IsAccount(h, me) && !selectable.Contains(me),
            CurrentUserId = me.ToString("N"),
            PinLength = Math.Clamp(c.PinLength, 4, 8),
            Profiles = profiles
        });
    }

    /// <summary>Bascule la session courante sur un profil du même foyer.</summary>
    /// <param name="profileId">Profil cible.</param>
    /// <param name="body">PIN éventuel.</param>
    /// <returns>AuthenticationResult (même format que /Users/AuthenticateByName).</returns>
    [HttpPost("Profiles/{profileId}/Switch")]
    [Authorize]
    public async Task<ActionResult> SwitchProfile([FromRoute] Guid profileId, [FromBody] PinRequest? body)
    {
        var auth = await _authContext.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        var me = auth.UserId;
        var h = me == Guid.Empty ? null : _households.FindForUser(me);
        if (h is null || !HouseholdService.SelectableIds(h).Contains(profileId))
        {
            return ToJson(new { Error = "forbidden", Message = "Ce profil n'appartient pas à ton foyer." }, StatusCodes.Status403Forbidden);
        }

        var locked = _households.LockedFor(profileId);
        if (locked is not null)
        {
            return ToJson(new { Error = "locked", RetryAfter = (int)Math.Ceiling(locked.Value.TotalSeconds) }, StatusCodes.Status429TooManyRequests);
        }

        if (!_households.VerifyPin(profileId, body?.Pin))
        {
            _logger.LogWarning("[ProfilePicker] PIN incorrect pour le profil {Profile} depuis {Ip}", profileId, HttpContext.Connection.RemoteIpAddress);

            // 422 et non 401 : un 401 déclencherait la déconnexion globale de jellyfin-web.
            return ToJson(new { Error = "pin" }, StatusCodes.Status422UnprocessableEntity);
        }

        var user = _userManager.GetUserById(profileId);
        if (user is null || _userManager.GetUserDto(user).Policy?.IsDisabled == true)
        {
            return ToJson(new { Error = "disabled", Message = "Profil désactivé." }, StatusCodes.Status403Forbidden);
        }

        try
        {
            var result = await _sessionManager.AuthenticateDirect(new AuthenticationRequest
            {
                UserId = profileId,
                DeviceId = string.IsNullOrWhiteSpace(auth.DeviceId) ? "profile-picker-" + me.ToString("N") : auth.DeviceId,
                DeviceName = string.IsNullOrWhiteSpace(auth.Device) ? "Navigateur" : auth.Device,
                App = string.IsNullOrWhiteSpace(auth.Client) ? "Jellyfin Web" : auth.Client,
                AppVersion = string.IsNullOrWhiteSpace(auth.Version) ? "0.0.0" : auth.Version,
                RemoteEndPoint = HttpContext.Connection.RemoteIpAddress?.ToString()
            }).ConfigureAwait(false);

            // Révoque le jeton précédent (compte foyer ou ancien profil) : une seule session par appareil.
            if (profileId != me && auth.HasToken && !auth.IsApiKey)
            {
                try
                {
                    await _sessionManager.Logout(auth.Token!).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[ProfilePicker] Révocation de l'ancien jeton impossible");
                }
            }

            _logger.LogInformation("[ProfilePicker] Foyer « {Household} » : bascule {From} -> {To}", h.Name, me, profileId);
            return Ok(result);
        }
        catch (Exception ex) when (ex is SecurityException or AuthenticationException)
        {
            return ToJson(new { Error = "denied", ex.Message }, StatusCodes.Status403Forbidden);
        }
    }

    /// <summary>Définit (ou supprime si vide) le PIN d'un profil.</summary>
    /// <param name="profileId">Profil.</param>
    /// <param name="body">PIN.</param>
    /// <returns>204.</returns>
    [HttpPost("Profiles/{profileId}/Pin")]
    [Authorize(Policy = Elevated)]
    public ActionResult SetProfilePin([FromRoute] Guid profileId, [FromBody] PinRequest? body)
    {
        var pin = body?.Pin?.Trim() ?? string.Empty;
        if (pin.Length > 0 && (pin.Length < 4 || pin.Length > 8 || !pin.All(char.IsAsciiDigit)))
        {
            return BadRequest("Le PIN doit contenir 4 à 8 chiffres.");
        }

        _households.SetPin(profileId, pin.Length == 0 ? null : pin);
        return NoContent();
    }

    /// <summary>État de l'injection du script dans jellyfin-web.</summary>
    /// <returns>JSON.</returns>
    [HttpGet("Status")]
    [Authorize(Policy = Elevated)]
    public ActionResult GetStatus() => ToJson(new
    {
        WebInjector.LastStatus.Injected,
        WebInjector.LastStatus.IndexPath,
        WebInjector.LastStatus.Message,
        CustomAvatarDirectory = _avatars.CustomDirectory,
        Version = typeof(ProfilePickerController).Assembly.GetName().Version?.ToString()
    });

    /// <summary>Relance l'injection.</summary>
    /// <returns>JSON.</returns>
    [HttpPost("Inject")]
    [Authorize(Policy = Elevated)]
    public ActionResult Reinject() => ToJson(_injector.Apply());

    private static ContentResult ToJson(object value, int status = StatusCodes.Status200OK) => new()
    {
        Content = JsonSerializer.Serialize(value, Json),
        ContentType = MediaTypeNames.Application.Json,
        StatusCode = status
    };
}

/// <summary>Corps de requête contenant un PIN.</summary>
public class PinRequest
{
    /// <summary>Code PIN.</summary>
    public string? Pin { get; set; }
}
