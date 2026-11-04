using System;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.ProfilePicker.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfilePickerController"/> class.
    /// </summary>
    /// <param name="avatars">Bibliothèque d'avatars.</param>
    /// <param name="injector">Injecteur web.</param>
    public ProfilePickerController(AvatarStore avatars, WebInjector injector)
    {
        _avatars = avatars;
        _injector = injector;
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

    private ContentResult ToJson(object value) => new()
    {
        Content = JsonSerializer.Serialize(value, Json),
        ContentType = MediaTypeNames.Application.Json,
        StatusCode = StatusCodes.Status200OK
    };
}
