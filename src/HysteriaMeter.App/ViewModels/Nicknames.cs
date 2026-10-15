using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Apodos que el usuario les pone a los jugadores (como en Details!). Solo cambian lo que se muestra.</summary>
public static class Nicknames
{
    /// <summary>Largo máximo de un apodo, para que entre en la fila.</summary>
    public const int MaxLength = 24;

    /// <summary>El encuentro con los nombres reemplazados por sus apodos; el mismo objeto si no hay ninguno que aplique.</summary>
    public static EncounterSnapshot Apply(EncounterSnapshot snapshot, IReadOnlyDictionary<string, string>? nicknames)
    {
        if (nicknames is null || nicknames.Count == 0 || !snapshot.Actors.Any(a => nicknames.ContainsKey(a.Name)))
        {
            return snapshot;
        }

        return snapshot with
        {
            Actors = [.. snapshot.Actors.Select(a => nicknames.TryGetValue(a.Name, out var alias) ? a with { Name = alias } : a)],
        };
    }

    /// <summary>Pone, cambia o (con texto vacío) saca el apodo de <paramref name="realName"/>.</summary>
    public static IReadOnlyDictionary<string, string> With(IReadOnlyDictionary<string, string>? nicknames, string realName, string? alias)
    {
        var result = new Dictionary<string, string>(nicknames ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        var clean = alias?.Trim() ?? string.Empty;
        if (clean.Length > MaxLength)
        {
            clean = clean[..MaxLength];
        }

        if (clean.Length == 0 || clean == realName)
        {
            result.Remove(realName);
        }
        else
        {
            result[realName] = clean;
        }

        return result;
    }
}
