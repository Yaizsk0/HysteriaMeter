using HysteriaMeter.App.Pipeline;
using HysteriaMeter.Combat;
using HysteriaMeter.Storage;

namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// Lo que comparten todas las ventanas del overlay: el motor, la grabación, el historial y las
/// preferencias generales. Cada ventana elige además qué muestra (daño, curación o ambos).
/// </summary>
public sealed class MeterSession
{
    private readonly Action<MeterPreferences> _save;

    public MeterSession(
        CombatEngine engine,
        CaptureRecorder recorder,
        SkillNames skillNames,
        MeterPreferences preferences,
        Action<MeterPreferences> save,
        EncounterRepository? history = null,
        CaptureStatus? status = null)
    {
        Engine = engine;
        Recorder = recorder;
        SkillNames = skillNames;
        History = history;
        Status = status ?? new CaptureStatus();
        Preferences = preferences;
        _save = save;
        engine.Scope = preferences.Scope;
        engine.SplitEncounters = preferences.SplitEncounters;
        ViewModels.Numbers.Style = preferences.Numbers;
    }

    public CombatEngine Engine { get; }

    public CaptureRecorder Recorder { get; }

    public SkillNames SkillNames { get; }

    public EncounterRepository? History { get; }

    public CaptureStatus Status { get; }

    public MeterPreferences Preferences { get; private set; }

    /// <summary>Nombres de las propiedades de <see cref="MeterViewModel"/> que hay que refrescar en todas las ventanas.</summary>
    public event Action<IReadOnlyList<string>>? PreferencesChanged;

    /// <summary>Se reinició el encuentro: todas las ventanas vacían sus filas.</summary>
    public event Action? ResetRequested;

    public void Update(MeterPreferences preferences, params string[] changed)
    {
        Preferences = preferences;
        ViewModels.Numbers.Style = preferences.Numbers;
        _save(preferences);
        PreferencesChanged?.Invoke(changed);
    }

    /// <summary>Borra las peleas comunes más viejas que lo elegido en configuración. Devuelve cuántas borró.</summary>
    public async Task<int> CleanupHistoryAsync(DateTimeOffset now)
    {
        if (History is null || Preferences.RetentionDays <= 0)
        {
            return 0;
        }

        await History.InitializeAsync().ConfigureAwait(false);
        return await History.DeleteOlderThanAsync(now.AddDays(-Preferences.RetentionDays)).ConfigureAwait(false);
    }

    public void Reset()
    {
        Engine.Reset();
        ResetRequested?.Invoke();
    }
}
