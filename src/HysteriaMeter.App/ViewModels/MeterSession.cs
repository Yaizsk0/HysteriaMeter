using HysteriaMeter.App.Pipeline;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Decoding;
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
        CaptureStatus? status = null,
        DecoderStats? decoder = null)
    {
        Engine = engine;
        Recorder = recorder;
        SkillNames = skillNames.Copy();
        SkillNames.Custom = preferences.SkillAliases ?? new Dictionary<uint, string>();
        History = history;
        Status = status ?? new CaptureStatus();
        Decoder = decoder ?? new DecoderStats();
        Preferences = preferences;
        _save = save;
        engine.Scope = preferences.Scope;
        engine.SplitEncounters = preferences.SplitEncounters;
        engine.IdleTimeout = TimeSpan.FromSeconds(preferences.IdleSeconds);
        ViewModels.Numbers.Style = preferences.Numbers;
        engine.EncounterFinished += OnEncounterFinished;
    }

    /// <summary>Resultado del último envío a Discord, para mostrarlo en configuración.</summary>
    public event Action<string>? DiscordResult;

    /// <summary>El encuentro como se comparte: con nombres anónimos si así se eligió.</summary>
    public EncounterSnapshot ForSharing(EncounterSnapshot snapshot) =>
        Preferences.AnonymizeExports ? EncounterExport.Anonymize(snapshot) : snapshot;

    /// <summary>Manda el resumen al webhook configurado y avisa el resultado.</summary>
    public async Task SendToDiscordAsync(EncounterSnapshot snapshot)
    {
        var summary = MeterViewModel.BuildSummary(ForSharing(snapshot), MeterTab.Damage);
        if (summary.Length == 0)
        {
            DiscordResult?.Invoke("No hay daño para mandar");
            return;
        }

        var error = await DiscordWebhook.SendAsync(Preferences.DiscordWebhook, summary).ConfigureAwait(false);
        DiscordResult?.Invoke(error ?? $"Enviado a Discord: {snapshot.TargetName ?? "Encuentro"}");
    }

    private async void OnEncounterFinished(EncounterSnapshot snapshot)
    {
        if (!Preferences.DiscordOnKill || !snapshot.TargetKilled || !DiscordWebhook.IsValidUrl(Preferences.DiscordWebhook))
        {
            return;
        }

        try
        {
            await SendToDiscordAsync(snapshot).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            DiscordResult?.Invoke($"No se pudo mandar a Discord: {ex.Message}");
        }
    }

    public CombatEngine Engine { get; }

    /// <summary>Reproduce el sonido de una alerta; <c>null</c> en pruebas.</summary>
    public Action<AlertSound>? PlaySound { get; init; }

    public CaptureRecorder Recorder { get; }

    public SkillNames SkillNames { get; }

    public EncounterRepository? History { get; }

    public CaptureStatus Status { get; }

    /// <summary>Contadores del decodificador, para el diagnóstico y el aviso de parche.</summary>
    public DecoderStats Decoder { get; }

    public MeterPreferences Preferences { get; private set; }

    private bool _noticeTaken;

    /// <summary>Las novedades, una sola vez por arranque: las ve la primera ventana que pregunta.</summary>
    public StartupNotice? TakeStartupNotice()
    {
        if (_noticeTaken)
        {
            return null;
        }

        _noticeTaken = true;
        return NewsFeed.NoticeFor(NewsFeed.Current, Preferences.LastSeenNews);
    }

    /// <summary>
    /// Nombres de las propiedades de <see cref="MeterViewModel"/> que hay que refrescar en todas las ventanas. La cadena
    /// vacía significa que cambió todo (por ejemplo, al importar un perfil): ver <see cref="Touches"/>.
    /// </summary>
    public event Action<IReadOnlyList<string>>? PreferencesChanged;

    /// <summary>Si un aviso de <see cref="PreferencesChanged"/> incluye <paramref name="property"/> o es "cambió todo".</summary>
    public static bool Touches(IReadOnlyList<string> changed, string property) =>
        changed.Contains(property) || changed.Contains(string.Empty);

    /// <summary>Se reinició el encuentro: todas las ventanas vacían sus filas.</summary>
    public event Action? ResetRequested;

    public void Update(MeterPreferences preferences, params string[] changed)
    {
        Preferences = preferences;
        SkillNames.Custom = preferences.SkillAliases ?? new Dictionary<uint, string>();
        ViewModels.Numbers.Style = preferences.Numbers;
        Engine.IdleTimeout = TimeSpan.FromSeconds(preferences.IdleSeconds);
        Engine.Scope = preferences.Scope;
        Engine.SplitEncounters = preferences.SplitEncounters;
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

/// <summary>Qué tipo de sonido lleva una alerta.</summary>
public enum AlertSound
{
    /// <summary>Algo bueno o informativo (récord, prueba terminada).</summary>
    Info,

    /// <summary>Algo que pide atención ya (no se llega al enrage).</summary>
    Warning,
}
