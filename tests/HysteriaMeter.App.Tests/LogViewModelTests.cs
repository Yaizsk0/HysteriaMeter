using System.Globalization;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.App.Tests;

/// <summary>El registro de la pelea: formato de las líneas, búsqueda y tiempos (ventana Registro y pestañas del detalle).</summary>
public class LogViewModelTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 21, 30, 0, TimeSpan.Zero);

    private static CombatLogEntry Entry(int ms, LogKind kind, ulong source, string sourceName, ulong target, string targetName, long amount, HitModifiers modifiers = HitModifiers.None) =>
        new(Start.AddMilliseconds(ms), kind, source, sourceName, target, targetName, 9001, amount, modifiers);

    private static readonly IReadOnlyList<CombatLogEntry> Fight =
    [
        Entry(65_000, LogKind.Damage, 1, "Kaelis", 900, "Kromede", 1_000, HitModifiers.Critical),
        Entry(66_000, LogKind.Damage, 900, "Kromede", 2, "Tharos", 2_000),
        Entry(68_000, LogKind.Heal, 3, "Morwen", 2, "Tharos", 500),
        Entry(71_000, LogKind.Damage, 3, "Morwen", 900, "Kromede", 700, HitModifiers.BackAttack),
    ];

    [Fact]
    public void A_hit_shows_its_modifiers_as_tags_and_a_heal_or_death_their_own_way()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);

        var hit = LogViewModel.Format(Entry(65_300, LogKind.Damage, 1, "Kaelis", 900, "Kromede", 41_000, HitModifiers.Critical | HitModifiers.BackAttack), Start, SkillNames.Empty);
        Assert.Equal("01:05.3", hit.Time);
        Assert.Equal(("crítico, espalda", "CRÍTICO · ESPALDA"), (hit.Flags, hit.Tags));
        Assert.Equal(("41.0k", "Skill 9001"), (hit.Amount, hit.DisplaySkill));
        Assert.Equal("01:05.3 · Kaelis → Kromede: Skill 9001 41.0k (crítico, espalda)", hit.Tip);
        Assert.Equal("DoT", LogViewModel.Tags(HitModifiers.DamageOverTime));

        var heal = LogViewModel.Format(Entry(1_000, LogKind.Heal, 3, "Morwen", 2, "Tharos", 20_000), Start, SkillNames.Empty);
        Assert.True(heal.IsHeal);
        Assert.Equal(("+20.0k", "curación", string.Empty), (heal.Amount, heal.Flags, heal.Tags));

        var death = LogViewModel.Format(Entry(71_000, LogKind.Death, 2, "Tharos", 2, "Tharos", 0), Start, SkillNames.Empty);
        Assert.True(death.IsDeath);
        Assert.Equal("☠ muerte", death.DisplaySkill);
        Assert.Equal("01:11.0 · murió Tharos", death.Tip);
    }

    [Fact]
    public void The_search_ignores_case_and_accents_and_the_player_counts_on_either_side()
    {
        var critical = Assert.Single(LogViewModel.Filter(Fight, SkillNames.Empty, "critico", null));
        Assert.Equal("Kaelis", critical.Source);
        Assert.Equal("Morwen", Assert.Single(LogViewModel.Filter(Fight, SkillNames.Empty, "  ESPALDA ", null)).Source);
        Assert.True(Assert.Single(LogViewModel.Filter(Fight, SkillNames.Empty, "curacion", null)).IsHeal);
        Assert.Equal(["Kromede", "Morwen"], LogViewModel.Filter(Fight, SkillNames.Empty, "tharos", null).Select(l => l.Source));

        // Tharos solo recibe: el golpe de Kromede y la curación de Morwen.
        Assert.Equal(2, LogViewModel.Filter(Fight, SkillNames.Empty, null, 2).Count);
        Assert.Equal(4, LogViewModel.Filter(Fight, SkillNames.Empty, string.Empty, null).Count);
    }

    [Fact]
    public void Times_count_from_the_fight_start_unless_it_comes_after_the_first_line()
    {
        Assert.Equal(["01:05.0", "01:06.0", "01:08.0", "01:11.0"], LogViewModel.Filter(Fight, SkillNames.Empty, null, null, fightStart: Start).Select(l => l.Time));
        Assert.Equal(["00:00.0", "00:01.0", "00:03.0", "00:06.0"], LogViewModel.Filter(Fight, SkillNames.Empty, null, null).Select(l => l.Time));

        // Un inicio posterior a la primera línea daría tiempos negativos: se cuenta desde la primera.
        Assert.Equal("00:00.0", LogViewModel.Filter(Fight, SkillNames.Empty, null, null, fightStart: Start.AddSeconds(70))[0].Time);

        var log = new LogViewModel(() => Fight, SkillNames.Empty, fightStart: () => Start);
        Assert.Equal("01:05.0", log.Lines[0].Time);
        Assert.Equal("00:00.0", new LogViewModel(() => Fight, SkillNames.Empty, fightStart: () => null).Lines[0].Time);
    }

    [Fact]
    public void Esc_clears_the_search_only_when_it_has_text()
    {
        var log = new LogViewModel(() => Fight, SkillNames.Empty, focusId: 2, focusName: "Tharos");

        Assert.True(log.HasFocus);
        Assert.True(log.OnlyFocus);
        Assert.Equal(2, log.Lines.Count);
        Assert.False(log.ClearSearchCommand.CanExecute(null));

        log.Search = "kromede";
        Assert.Single(log.Lines);
        Assert.True(log.ClearSearchCommand.CanExecute(null));

        log.ClearSearchCommand.Execute(null);
        Assert.Equal(string.Empty, log.Search);
        Assert.Equal(2, log.Lines.Count);

        log.OnlyFocus = false;
        Assert.Equal(4, log.Lines.Count);
        Assert.False(log.IsEmpty);
    }

    [Fact]
    public void Reloading_appends_the_new_lines_and_tells_the_view()
    {
        var entries = new List<CombatLogEntry>(Fight.Take(2));
        var log = new LogViewModel(() => [.. entries], SkillNames.Empty);
        var first = log.Lines[0];
        var changes = 0;
        log.LinesChanged += () => changes++;

        entries.Add(Fight[2]);
        log.Reload();

        Assert.Equal(3, log.Lines.Count);
        Assert.Same(first, log.Lines[0]); // sin rearmar la lista: el scroll queda donde estaba
        Assert.Equal(1, changes);

        entries.Clear();
        log.Reload();
        Assert.True(log.IsEmpty);
        Assert.Equal(2, changes);
    }
}
