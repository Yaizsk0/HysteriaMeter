using System.Reflection;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Versión del build que está corriendo, para saber si el overlay está actualizado.</summary>
public static class AppVersion
{
    /// <summary>Por ejemplo "v0.15.0 · a352982".</summary>
    public static string Text { get; } = Format(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>
    /// Arma el texto a partir de la versión informativa del SDK ("0.15.0+&lt;sha completo&gt;"):
    /// el commit se acorta a 7 caracteres, como en <c>git log --oneline</c>.
    /// </summary>
    public static string Format(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "versión desconocida";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            return $"v{informational}";
        }

        var commit = informational[(plus + 1)..];
        return $"v{informational[..plus]} · {commit[..Math.Min(commit.Length, 7)]}";
    }
}
