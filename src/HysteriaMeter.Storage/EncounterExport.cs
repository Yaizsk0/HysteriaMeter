using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HysteriaMeter.Combat;

namespace HysteriaMeter.Storage;

/// <summary>Exporta un encuentro a CSV (para planillas) o JSON (para otras herramientas). Sin I/O: devuelve el texto.</summary>
public static class EncounterExport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Reemplaza los nombres: el jugador local queda como "Yo" y el resto como "Jugador 1", "Jugador 2"... por daño.
    /// Para compartir sin exponer a la party.
    /// </summary>
    public static EncounterSnapshot Anonymize(EncounterSnapshot encounter)
    {
        var index = 0;
        var actors = encounter.Actors
            .OrderByDescending(a => a.Damage)
            .Select(a => a with { Name = a.IsLocalPlayer ? "Yo" : $"Jugador {++index}" })
            .ToArray();
        return encounter with { Actors = actors };
    }

    /// <summary>Una fila por jugador. Separador ";" y números sin separador de miles, como los abre Excel en español.</summary>
    public static string ToCsv(EncounterSnapshot encounter)
    {
        var inv = CultureInfo.InvariantCulture;
        var seconds = encounter.Duration.TotalSeconds;
        var csv = new StringBuilder();
        csv.AppendLine("objetivo;inicio;duracion_s;derrotado;jugador;local;danio;dps;dps_activo;porcentaje;golpes;criticos;golpe_maximo;curacion;hps;dot;overkill");
        foreach (var a in encounter.Actors.OrderByDescending(a => a.Damage))
        {
            csv.AppendJoin(
                ';',
                Field(encounter.TargetName ?? "Encuentro"),
                encounter.Start.ToString("O", inv),
                seconds.ToString("0.0", inv),
                encounter.TargetKilled ? "si" : "no",
                Field(a.Name),
                a.IsLocalPlayer ? "si" : "no",
                a.Damage.ToString(inv),
                a.Dps.ToString("0", inv),
                a.ActiveDps.ToString("0", inv),
                (a.Share * 100).ToString("0.0", inv),
                a.Hits.ToString(inv),
                a.Crits.ToString(inv),
                a.MaxHit.ToString(inv),
                a.Healing.ToString(inv),
                (a.Healing / seconds).ToString("0", inv),
                a.PeriodicDamage.ToString(inv),
                a.Overkill.ToString(inv));
            csv.AppendLine();
        }

        return csv.ToString();
    }

    /// <summary>El encuentro completo (jugadores, skills, línea de tiempo) en JSON indentado.</summary>
    public static string ToJson(EncounterSnapshot encounter) => JsonSerializer.Serialize(encounter, JsonOptions);

    /// <summary>
    /// Lee un encuentro exportado con <see cref="ToJson"/> (propio o de otro Hysteria Meter), o <c>null</c> si el
    /// texto no es un encuentro válido. Las listas que falten quedan vacías.
    /// </summary>
    public static EncounterSnapshot? FromJson(string json)
    {
        try
        {
            if (JsonSerializer.Deserialize<EncounterSnapshot>(json, JsonOptions) is not { } encounter || encounter.Start == default || encounter.End < encounter.Start)
            {
                return null;
            }

            var actors = (encounter.Actors ?? []).Where(a => a is not null)
                .Select(a => a with { Name = a.Name ?? "?", Skills = a.Skills ?? [] })
                .ToArray();
            return actors.Length == 0 ? null : encounter with { Actors = actors };
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>"Destroyer Tarka 2026-10-15 21-42": nombre de archivo sin caracteres prohibidos en Windows.</summary>
    public static string FileName(EncounterSnapshot encounter, DateTimeOffset localStart)
    {
        var target = encounter.TargetName ?? "Encuentro";
        var invalid = new HashSet<char>(['<', '>', ':', '"', '/', '\\', '|', '?', '*']);
        var safe = new string([.. target.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c)]).Trim();
        return $"{(safe.Length == 0 ? "Encuentro" : safe)} {localStart.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture)}";
    }

    private static string Field(string value) =>
        value.Contains(';', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
}
