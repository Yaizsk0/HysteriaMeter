using System.Globalization;
using System.Text;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// Reporte de un problema con un clic: versión, sistema, estado de la captura, diagnóstico y verificación, sin nombres de
/// personajes ni de NPC ni el webhook. Se copia al portapapeles y se abre un issue nuevo en GitHub con eso adentro.
/// </summary>
public static class BugReport
{
    public const string IssuesUrl = "https://github.com/Yaizsk0/HysteriaMeter/issues/new";

    /// <param name="fight">La pelea de la verificación (la de Diagnóstico), o <c>null</c> sin pelea.</param>
    public static string Build(string version, string os, string status, string diagnostics, MeterPreferences preferences, EncounterSnapshot? fight)
    {
        var text = new StringBuilder();
        text.AppendLine("**Qué pasó:** (contalo acá; si podés, adjuntá una grabación .pcap por privado, no en el issue público)");
        text.AppendLine();
        text.AppendLine("---");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Versión: {version}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Sistema: {os}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Captura: {Clean(status)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Diagnóstico: {Clean(diagnostics)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Verificación: {Verification(fight)}");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"- Preferencias: filtro {preferences.Scope}, separar peleas {preferences.SplitEncounters} ({preferences.IdleSeconds} s), refresco {preferences.RefreshMs} ms, tema {preferences.Theme}");
        return text.ToString();
    }

    /// <summary>
    /// "Cierran 2 de 3 objetivos · principal NPC 2400123: no cierra, diferencia -2.000 (-25,0 % de la vida perdida)".
    /// Solo plantillas y números: el issue es público, así que ni el nombre del NPC (de data/npcs.json) ni el de nadie.
    /// Sin verificación del principal, solo la cuenta; sin pelea o sin verificación, "–".
    /// </summary>
    public static string Verification(EncounterSnapshot? fight)
    {
        if (fight?.Verifications is not { Count: > 0 } verifications)
        {
            return "–";
        }

        var count = MeterViewModel.CountText(verifications);
        if (MeterViewModel.MainVerification(fight) is not { NpcId: { } npc } main)
        {
            return count;
        }

        var result = main.Closes ? "cierra" : "no cierra";
        return $"{count} · principal {FightTarget.NpcLabel(npc)}: {result}, {MeterViewModel.DifferenceText(main, CultureInfo.CurrentCulture)}";
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
