using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Una línea del registro, ya formateada para la lista.</summary>
public sealed record LogLine(string Time, string Source, string Target, string Skill, string Amount, string Flags, LogKind Kind);

/// <summary>Registro de la pelea (estilo WarcraftLogs): quién, a quién, skill, monto y modificadores, con búsqueda.</summary>
public sealed partial class LogViewModel : ObservableObject
{
    /// <summary>Líneas que se muestran como máximo, las más nuevas.</summary>
    public const int MaxLines = 5_000;

    private readonly Func<IReadOnlyList<CombatLogEntry>> _source;
    private readonly SkillNames _names;
    private IReadOnlyList<CombatLogEntry> _entries = [];

    public LogViewModel(Func<IReadOnlyList<CombatLogEntry>> source, SkillNames names, ulong? focusId = null, string? focusName = null)
    {
        _source = source;
        _names = names;
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
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool OnlyFocus { get; set; }

    [ObservableProperty]
    public partial string CountText { get; set; } = string.Empty;

    partial void OnSearchChanged(string value) => Apply();

    partial void OnOnlyFocusChanged(bool value) => Apply();

    /// <summary>Vuelve a leer el registro del motor (la pelea sigue sumando líneas).</summary>
    [RelayCommand]
    private void Reload()
    {
        _entries = _source();
        Apply();
    }

    private void Apply()
    {
        var lines = Filter(_entries, _names, Search, OnlyFocus ? FocusId : null);
        Lines.Clear();
        foreach (var line in lines.Skip(Math.Max(0, lines.Count - MaxLines)))
        {
            Lines.Add(line);
        }

        CountText = lines.Count > MaxLines
            ? $"{MaxLines:N0} de {lines.Count:N0} líneas (las más nuevas)"
            : $"{lines.Count:N0} líneas";
    }

    /// <summary>Formatea y filtra: por jugador (como origen u objetivo) y por texto en cualquier columna.</summary>
    public static IReadOnlyList<LogLine> Filter(IReadOnlyList<CombatLogEntry> entries, SkillNames names, string? search, ulong? actorId)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var start = entries[0].Timestamp;
        var text = search?.Trim();
        var result = new List<LogLine>();
        foreach (var entry in entries)
        {
            if (actorId is { } id && entry.SourceId != id && entry.TargetId != id)
            {
                continue;
            }

            var line = Format(entry, start, names);
            if (!string.IsNullOrEmpty(text)
                && !line.Source.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                && !line.Target.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                && !line.Skill.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                && !line.Flags.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            result.Add(line);
        }

        return result;
    }

    public static LogLine Format(CombatLogEntry entry, DateTimeOffset start, SkillNames names)
    {
        var offset = entry.Timestamp - start;
        var time = $"{(int)offset.TotalMinutes:00}:{offset.Seconds:00}.{offset.Milliseconds / 100}";
        return entry.Kind switch
        {
            LogKind.Death => new LogLine(time, entry.SourceName, string.Empty, "murió", string.Empty, string.Empty, entry.Kind),
            LogKind.Heal => new LogLine(time, entry.SourceName, entry.TargetName, names.Of(entry.SkillId), "+" + ActorRowViewModel.Compact(entry.Amount), "curación", entry.Kind),
            _ => new LogLine(time, entry.SourceName, entry.TargetName, names.Of(entry.SkillId), ActorRowViewModel.Compact(entry.Amount), Flags(entry.Modifiers), entry.Kind),
        };
    }

    /// <summary>"crítico, espalda": los modificadores con los nombres de Combat Analysis.</summary>
    public static string Flags(HitModifiers modifiers)
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
        return string.Join(", ", parts);
    }
}
