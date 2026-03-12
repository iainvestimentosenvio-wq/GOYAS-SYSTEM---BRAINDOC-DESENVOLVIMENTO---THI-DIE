using System;
using System.Collections.Concurrent;
using System.Net.Mail;

namespace Protons.Core.Login.Validation;

public static class EmailValidator
{
    // Cache pequeno e limitado para reduzir custo de validações repetidas
    // em fluxos de login/cadastro sem crescer memória indefinidamente.
    private const int MaxCacheEntries = 4096;
    private static readonly ConcurrentDictionary<string, bool> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool EhValido(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var normalizado = email.Trim();
        if (normalizado.Length == 0)
            return false;

        if (Cache.TryGetValue(normalizado, out var cached))
            return cached;

        var valido = MailAddress.TryCreate(normalizado, out _);
        ArmazenarCache(normalizado, valido);
        return valido;
    }

    private static void ArmazenarCache(string emailNormalizado, bool valido)
    {
        if (Cache.Count >= MaxCacheEntries)
        {
            Cache.Clear();
        }

        Cache.TryAdd(emailNormalizado, valido);
    }
}
