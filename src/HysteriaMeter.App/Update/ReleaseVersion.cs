using System.Globalization;

namespace HysteriaMeter.App.Update;

/// <summary>
/// Versión SemVer de un release ("0.16.0-alpha.1"), con la "v" del tag opcional. Lo que va después de "+" (el commit que
/// el SDK suma a la versión informativa) no cuenta para comparar.
/// </summary>
public sealed record ReleaseVersion(int Major, int Minor, int Patch, string Prerelease)
{
    public bool IsPrerelease => Prerelease.Length > 0;

    /// <summary>"0.16.0-alpha.1", "v0.16.0" o "0.16.0-alpha.1+&lt;sha&gt;"; <c>null</c> si no es una versión.</summary>
    public static ReleaseVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            value = value[..plus];
        }

        var dash = value.IndexOf('-', StringComparison.Ordinal);
        var core = (dash < 0 ? value : value[..dash]).Split('.');
        var prerelease = dash < 0 ? string.Empty : value[(dash + 1)..];
        if (core.Length != 3 || !TryNumber(core[0], out var major) || !TryNumber(core[1], out var minor) || !TryNumber(core[2], out var patch))
        {
            return null;
        }

        if (dash >= 0 && prerelease.Split('.').Any(part => part.Length == 0 || !part.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
        {
            return null;
        }

        return new ReleaseVersion(major, minor, patch, prerelease);
    }

    /// <summary>
    /// Negativo si <paramref name="a"/> es anterior, 0 si es la misma versión y positivo si es posterior. Como SemVer:
    /// 1.0.0-alpha &lt; 1.0.0-alpha.1 &lt; 1.0.0-alpha.beta &lt; 1.0.0-beta.2 &lt; 1.0.0-beta.11 &lt; 1.0.0.
    /// </summary>
    public static int Compare(ReleaseVersion a, ReleaseVersion b)
    {
        var core = a.Major != b.Major ? a.Major.CompareTo(b.Major)
            : a.Minor != b.Minor ? a.Minor.CompareTo(b.Minor)
            : a.Patch.CompareTo(b.Patch);
        if (core != 0 || (!a.IsPrerelease && !b.IsPrerelease))
        {
            return core;
        }

        if (a.IsPrerelease != b.IsPrerelease)
        {
            return a.IsPrerelease ? -1 : 1;
        }

        var left = a.Prerelease.Split('.');
        var right = b.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var order = (TryNumber(left[i], out var l), TryNumber(right[i], out var r)) switch
            {
                (true, true) => l.CompareTo(r),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(left[i], right[i]),
            };
            if (order != 0)
            {
                return order;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    public bool IsNewerThan(ReleaseVersion other) => Compare(this, other) > 0;

    public override string ToString() => IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Prerelease}" : $"{Major}.{Minor}.{Patch}";

    private static bool TryNumber(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
