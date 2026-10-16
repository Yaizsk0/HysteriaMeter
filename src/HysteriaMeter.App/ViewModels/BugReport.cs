using System.Globalization;
using System.Text;

namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// Reporte de un problema con un clic: versión, sistema, estado de la captura y diagnóstico, sin nombres de
/// personajes ni el webhook. Se copia al portapapeles y se abre un issue nuevo en GitHub con eso adentro.
/// </summary>
public static class BugReport
{
    public const string IssuesUrl = "https://github.com/Yaizsk0/HysteriaMeter/issues/new";

    public static string Build(string version, string os, string status, string diagnostics, MeterPreferences preferences)
    {
        var text = new StringBuilder();
        text.AppendLine("**Qué pasó:** (contalo acá; si podés, adjuntá una grabación .pcap por privado, no en el issue público)");
        text.AppendLine();
        text.AppendLine("---");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Versión: {version}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Sistema: {os}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Captura: {Clean(status)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Diagnóstico: {Clean(diagnostics)}");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"- Preferencias: filtro {preferences.Scope}, separar peleas {preferences.SplitEncounters} ({preferences.IdleSeconds} s), refresco {preferences.RefreshMs} ms, tema {preferences.Theme}");
        return text.ToString();
    }

    /// <summary>URL del issue nuevo con título y cuerpo; GitHub corta las URLs muy largas, así que el cuerpo se limita.</summary>
    public static string IssueUrl(string body)
    {
        const int MaxBody = 3000;
        var trimmed = body.Length > MaxBody ? body[..MaxBody] : body;
        return $"{IssuesUrl}?title={Uri.EscapeDataString("Problema: ")}&body={Uri.EscapeDataString(trimmed)}";
    }

    private static string Clean(string text) => string.IsNullOrWhiteSpace(text) ? "–" : text.ReplaceLineEndings(" ").Trim();
}
