using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.ProfilePicker.Configuration;

namespace Jellyfin.Plugin.ProfilePicker.Services;

/// <summary>
/// Résolution des foyers et vérification des codes PIN de profil.
/// </summary>
public class HouseholdService
{
    /// <summary>Échecs autorisés avant blocage temporaire d'un profil.</summary>
    public const int MaxFailures = 5;

    /// <summary>Durée du blocage après trop d'échecs.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    private const int Iterations = 120_000;
    private readonly ConcurrentDictionary<Guid, (int Failures, DateTime Until)> _failures = new();

    private static PluginConfiguration Config => Plugin.Instance!.Configuration;

    /// <summary>Foyer auquel appartient un utilisateur (compte ou profil).</summary>
    /// <param name="userId">Utilisateur.</param>
    /// <returns>Foyer ou null.</returns>
    public Household? FindForUser(Guid userId)
        => Config.Households.FirstOrDefault(h => Same(h.AccountUserId, userId) || h.ProfileUserIds.Any(p => Same(p, userId)));

    /// <summary>Le compte de connexion du foyer est-il cet utilisateur ?</summary>
    /// <param name="h">Foyer.</param>
    /// <param name="userId">Utilisateur.</param>
    /// <returns>Vrai si compte foyer.</returns>
    public static bool IsAccount(Household h, Guid userId) => Same(h.AccountUserId, userId);

    /// <summary>Ids sélectionnables dans un foyer, dans l'ordre d'affichage.</summary>
    /// <param name="h">Foyer.</param>
    /// <returns>Ids.</returns>
    public static IEnumerable<Guid> SelectableIds(Household h)
    {
        var ids = new List<Guid>();
        if (h.AccountIsProfile && Guid.TryParse(h.AccountUserId, out var acc))
        {
            ids.Add(acc);
        }

        foreach (var p in h.ProfileUserIds)
        {
            if (Guid.TryParse(p, out var g) && !ids.Contains(g))
            {
                ids.Add(g);
            }
        }

        return ids;
    }

    /// <summary>Le profil a-t-il un code PIN ?</summary>
    /// <param name="userId">Profil.</param>
    /// <returns>Vrai si verrouillé.</returns>
    public bool HasPin(Guid userId) => FindLock(userId) is not null;

    /// <summary>Temps restant de blocage.</summary>
    /// <param name="userId">Profil.</param>
    /// <returns>Durée ou null.</returns>
    public TimeSpan? LockedFor(Guid userId)
    {
        if (_failures.TryGetValue(userId, out var f) && f.Until > DateTime.UtcNow)
        {
            return f.Until - DateTime.UtcNow;
        }

        return null;
    }

    /// <summary>Vérifie le PIN (temps constant, avec anti-bruteforce).</summary>
    /// <param name="userId">Profil.</param>
    /// <param name="pin">PIN saisi.</param>
    /// <returns>Vrai si valide ou si aucun PIN n'est défini.</returns>
    public bool VerifyPin(Guid userId, string? pin)
    {
        var l = FindLock(userId);
        if (l is null)
        {
            return true;
        }

        var ok = false;
        try
        {
            var salt = Convert.FromBase64String(l.Salt);
            var expected = Convert.FromBase64String(l.Hash);
            var actual = Derive(pin ?? string.Empty, salt);
            ok = CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            ok = false;
        }

        if (ok)
        {
            _failures.TryRemove(userId, out _);
            return true;
        }

        _failures.AddOrUpdate(
            userId,
            _ => (1, DateTime.MinValue),
            (_, f) =>
            {
                if (f.Until > DateTime.UtcNow)
                {
                    return f; // déjà bloqué : on ne raccourcit pas le blocage
                }

                var n = f.Failures + 1;
                return n >= MaxFailures ? (0, DateTime.UtcNow + LockoutDuration) : (n, DateTime.MinValue);
            });
        return false;
    }

    /// <summary>Définit ou supprime le PIN d'un profil.</summary>
    /// <param name="userId">Profil.</param>
    /// <param name="pin">Nouveau PIN (null/vide = suppression).</param>
    public void SetPin(Guid userId, string? pin)
    {
        var cfg = Config;
        cfg.ProfileLocks.RemoveAll(l => Same(l.UserId, userId));
        if (!string.IsNullOrEmpty(pin))
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            cfg.ProfileLocks.Add(new ProfileLock
            {
                UserId = userId.ToString("N"),
                Salt = Convert.ToBase64String(salt),
                Hash = Convert.ToBase64String(Derive(pin, salt))
            });
        }

        _failures.TryRemove(userId, out _);
        Plugin.Instance!.SaveConfiguration();
    }

    private ProfileLock? FindLock(Guid userId) => Config.ProfileLocks.FirstOrDefault(l => Same(l.UserId, userId));

    private static byte[] Derive(string pin, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, 32);

    private static bool Same(string? s, Guid id) => Guid.TryParse(s, out var g) && g == id;
}
