using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Una línea del registro, ya formateada para la lista.</summary>
public sealed record LogLine(string Time, string Source, string Target, string Skill, string Amount, string Flags, LogKind Kind)
{
    /// <summary>Cuándo pasó, para ubicar una muerte en el registro.</summary>
    public DateTimeOffset Timestamp { get; init; }

    public ulong SourceId { get; init; }

    /// <summary>Color de quien lo hizo (el de su fila del overlay); <c>null</c> para los monstruos.</summary>
    public Brush? SourceBrush { get; init; }

    /// <summary>"CRÍTICO · ESPALDA": los modificadores como etiquetas, en el color del acento.</summary>
    public string Tags { get; init; } = string.Empty;

    public bool IsHeal => Kind == LogKind.Heal;

    public bool IsDeath => Kind == LogKind.Death;

    /// <summary>La skill o, en una muerte, "☠ muerte" como en el mock.</summary>
    public string DisplaySkill => IsDeath ? "☠ muerte" : Skill;

    /// <summary>La línea completa, con el objetivo que la vista angosta no muestra.</summary>
    public string Tip => IsDeath
        ? $"{Time} · murió {Source}"
        : $"{Time} · {Source} → {Target}: {Skill} {Amount}{(Flags.Length > 0 ? $" ({Flags})" : string.Empty)}";
}

/// <summary>Registro de la pelea (estilo WarcraftLogs): quién, a quién, skill, monto y modificadores, con búsqueda.</summary>
public sealed partial class LogViewModel : ObservableObject
{
    /// <summary>Líneas que se muestran como máximo, las más nuevas.</summary>
    public const int MaxLines = 5_000;

    private readonly Func<IReadOnlyList<CombatLogEntry>> _source;
    private readonly SkillNames _names;
    private readonly Func<ulong, Brush?>? _brushOf;
    private readonly Func<DateTimeOffset?>? _fightStart;
    private IReadOnlyList<CombatLogEntry> _entries = [];

    /// <param name="brushOf">Color de cada jugador por ID (el de su fila); sin él, los nombres van en el color del texto.</param>
    /// <param name="fightStart">
    /// Inicio de la pelea, para que los tiempos coincidan con los del detalle ("muerte a la 1:11"); sin él, cuentan
    /// desde la primera línea del registro.
    /// </param>
    public LogViewModel(
        Func<IReadOnlyList<CombatLogEntry>> source, SkillNames names, ulong? focusId = null, string? focusName = null, Func<ulong, Brush?>? brushOf = null,
        Func<DateTimeOffset?>? fightStart = null)
    {
        _source = source;
        _names = names;
        _brushOf = brushOf;
        _fightStart = fightStart;
        FocusId = focusId;
        FocusName = focusName ?? string.Empty;
        OnlyFocus = focusId is not null;
        Reload();
    }

    public ulong? FocusId { get; }

    public string FocusName { get; }

    public bool HasFocus => FocusId is not null;

    public ObservableCollection<LogLine> Lines { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearSearchCommand))]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool OnlyFocus { get; set; }

    [ObservableProperty]
    public partial string CountText { get; set; } = string.Empty;

    /// <summary>Sin líneas que mostrar (registro vacío o filtro sin resultados).</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    /// <summary>Las líneas cambiaron (se rearmó la lista o llegaron nuevas): la vista vuelve a ubicar lo que sigue.</summary>
    public event Action? LinesChanged;

    partial void OnSearchChanged(string value) => Apply();

    partial void OnOnlyFocusChanged(bool value) => Apply();

    /// <summary>Vuelve a leer el registro del motor (la pelea sigue sumando líneas).</summary>
    [RelayCommand]
    public void Reload()
    {
        _entries = _source();
        Apply();
    }

    /// <summary>Esc en la caja de búsqueda la vacía; si ya está vacía, Esc sigue de largo y cierra la ventana.</summary>
    [RelayCommand(CanExecute = nameof(CanClearSearch))]
    private void ClearSearch() => Search = string.Empty;

    private bool CanClearSearch() => Search.Length > 0;

    private void Apply()
    {
        var lines = Filter(_entries, _names, Search, OnlyFocus ? FocusId : null, _brushOf, _fightStart?.Invoke());
        var skip = Math.Max(0, lines.Count - MaxLines);
        var shown = lines.Count - skip;

        // En vivo casi siempre solo se agregan líneas al final: sumarlas sin rearmar la lista conserva el scroll.
        var appends = skip == 0 && Lines.Count > 0 && Lines.Count <= shown && Lines[0] == lines[0] && Lines[^1] == lines[Lines.Count - 1];
        if (!appends)
        {
            Lines.Clear();
        }

        for (var i = skip + Lines.Count; i < lines.Count; i++)
        {
            Lines.Add(lines[i]);
        }

        IsEmpty = Lines.Count == 0;
        CountText = lines.Count > MaxLines
            ? $"{MaxLines:N0} de {lines.Count:N0} líneas (las más nuevas)"
            : $"{lines.Count:N0} líneas";
        LinesChanged?.Invoke();
    }

    /// <summary>
    /// Formatea y filtra: por jugador (como origen u objetivo) y por texto en cualquier columna, sin distinguir
    /// mayúsculas ni tildes ("crit" encuentra "crítico"). Los tiempos cuentan desde <paramref name="fightStart"/> o, sin
    /// él (o si es posterior a la primera línea), desde la primera línea.
    /// </summary>
    public static IReadOnlyList<LogLine> Filter(
        IReadOnlyList<CombatLogEntry> entries, SkillNames names, string? search, ulong? actorId, Func<ulong, Brush?>? brushOf = null, DateTimeOffset? fightStart = null)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var start = fightStart is { } begin && begin <= entries[0].Timestamp ? begin : entries[0].Timestamp;
        var text = search?.Trim();
        var result = new List<LogLine>();
        foreach (var entry in entries)
        {
            if (actorId is { } id && entry.SourceId != id && entry.TargetId != id)
            {
                continue;
            }

            var line = Format(entry, start, names, brushOf);
            if (!string.IsNullOrEmpty(text)
                && !Matches(line.Source, text)
                && !Matches(line.Target, text)
                && !Matches(line.Skill, text)
                && !Matches(line.Flags, text)
                && !Matches(line.Tags, text))
            {
                continue;
            }

            result.Add(line);
        }

        return result;
    }

    private static bool Matches(string value, string text) =>
        CultureInfo.CurrentCulture.CompareInfo.IndexOf(value, text, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    public static LogLine Format(CombatLogEntry entry, DateTimeOffset start, SkillNames names, Func<ulong, Brush?>? brushOf = null)
    {
        var offset = entry.Timestamp - start;
        var time = $"{(int)offset.TotalMinutes:00}:{offset.Seconds:00}.{offset.Milliseconds / 100}";
        var line = entry.Kind switch
        {
            LogKind.Death => new LogLine(time, entry.SourceName, string.Empty, "murió", string.Empty, string.Empty, entry.Kind),
            LogKind.Heal => new LogLine(time, entry.SourceName, entry.TargetName, names.Of(entry.SkillId), "+" + ActorRowViewModel.Compact(entry.Amount), "curación", entry.Kind),
            _ => new LogLine(time, entry.SourceName, entry.TargetName, names.Of(entry.SkillId), ActorRowViewModel.Compact(entry.Amount), Flags(entry.Modifiers), entry.Kind)
            {
                Tags = Tags(entry.Modifiers),
            },
        };
        return line with { Timestamp = entry.Timestamp, SourceId = entry.SourceId, SourceBrush = brushOf?.Invoke(entry.SourceId) };
    }

    /// <summary>"crítico, espalda": los modificadores con los nombres de Combat Analysis.</summary>
    public static string Flags(HitModifiers modifiers) => string.Join(", ", FlagNames(modifiers));

    /// <summary>"CRÍTICO · ESPALDA": los mismos modificadores como etiquetas del log del mock ("DoT" queda como está).</summary>
    public static string Tags(HitModifiers modifiers) =>
        string.Join(" · ", FlagNames(modifiers).Select(name => name == "DoT" ? name : name.ToUpper(CultureInfo.CurrentCulture)));

    private static List<string> FlagNames(HitModifiers modifiers)
    {
        var parts = new List<string>(4);
        void Add(HitModifiers flag, string name)
        {
            if (modifiers.HasFlag(flag))
            {
                parts.Add(name);
            }
        }

        Add(HitModifiers.Critical, "crítico");
        Add(HitModifiers.Perfect, "perfecto");
        Add(HitModifiers.DoubleHit, "doble");
        Add(HitModifiers.BackAttack, "espalda");
        Add(HitModifiers.FrontAttack, "frente");
        Add(HitModifiers.MultiHit, "multi");
        Add(HitModifiers.DamageOverTime, "DoT");
        return parts;
    }
}
