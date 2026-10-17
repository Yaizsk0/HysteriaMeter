using System.Globalization;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.App.Tests;

/// <summary>El detalle de jugador del rediseño: KPIs, gráfico, Muertes, registro integrado, Comparar y renombrar skill.</summary>
public class ActorDetailViewModelTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 21, 30, 0, TimeSpan.Zero);

    private const ulong Boss = 900;

    private static SkillSnapshot Skill(uint id, long damage, int hits) => new(id, damage, hits, 0, damage / Math.Max(1, hits), 0);

    private static ActorSnapshot Actor(ulong id, string name, bool local = false, double dps = 100, SkillSnapshot[]? skills = null) =>
        new(id, name, null, local, (skills ?? []).Sum(s => s.Damage), dps, 0.5, (skills ?? []).Sum(s => s.Hits), 0, 0, 0, skills ?? []);

    private static EncounterSnapshot Fight(Guid id, IReadOnlyList<DeathRecap>? deaths, params ActorSnapshot[] actors) =>
        new(id, Start, Start.AddSeconds(100), "Kromede", true, actors.Sum(a => a.Damage), actors, Deaths: deaths);

    private static CombatLogEntry Hit(int second, ulong target, long amount, HitModifiers modifiers = HitModifiers.None, uint skill = 9101) =>
        new(Start.AddSeconds(second), LogKind.Damage, Boss, "Kromede", target, "Tharos", skill, amount, modifiers);

    private static CombatLogEntry Heal(int second, ulong target, long amount) =>
        new(Start.AddSeconds(second), LogKind.Heal, 3, "Morwen", target, "Tharos", 9201, amount);

    /// <summary>El detalle abierto sobre un jugador de la pelea, como lo deja el overlay al hacer clic en su fila.</summary>
    private static ActorDetailViewModel DetailOf(EncounterSnapshot fight, ulong id, SkillNames? names = null, Action<ActorDetailViewModel>? setup = null)
    {
        var detail = new ActorDetailViewModel(names ?? SkillNames.Empty);
        setup?.Invoke(detail);
        var row = new ActorRowViewModel();
        row.Update(1, fight.Actors.Single(a => a.Id == id), MeterTab.Damage, 1, 1, fight.Duration);
        detail.Select(row);
        detail.Update(fight);
        return detail;
    }

    // ===================== KPIs y gráfico =====================

    [Fact]
    public void The_chart_groups_seconds_into_bars_relative_to_the_highest_and_marks_them()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);

        var bars = ActorDetailViewModel.DpsBars([10, 20, 30, 40], marks: [3], count: 2);

        Assert.Equal([15 / 35.0, 1.0], bars.Select(b => b.Height));
        Assert.Equal([false, true], bars.Select(b => b.IsMarked));
        Assert.Equal("0:00–0:02 · 15 DPS", bars[0].Tip);
        Assert.Equal("0:02–0:04 · 35 DPS · marcado", bars[1].Tip);

        // Una marca justo en el borde va al tramo que empieza ahí; con menos segundos que barras, una por segundo.
        Assert.Equal([false, true], ActorDetailViewModel.DpsBars([10, 20, 30, 40], [2], 2).Select(b => b.IsMarked));
        Assert.Equal(3, ActorDetailViewModel.DpsBars([1, 2, 3], null, ActorDetailViewModel.ChartBarCount).Count);
        Assert.Empty(ActorDetailViewModel.DpsBars([], null, ActorDetailViewModel.ChartBarCount));
        Assert.All(ActorDetailViewModel.DpsBars([0, 0], null, 2), b => Assert.Equal(0.0, b.Height));
    }

    [Fact]
    public void Differences_against_the_previous_attempt_ignore_tiny_changes()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);

        Assert.Equal(new Delta("+10% vs intento anterior", DeltaTone.Better), ActorDetailViewModel.PercentDelta(110, 100));
        Assert.Equal(new Delta("−5% vs intento anterior", DeltaTone.Worse), ActorDetailViewModel.PercentDelta(95, 100));
        Assert.Equal(new Delta("igual que el intento anterior"), ActorDetailViewModel.PercentDelta(100.5, 100));
        Assert.Equal(Delta.None, ActorDetailViewModel.PercentDelta(100, 0));

        Assert.Equal(new Delta("+5 pts", DeltaTone.Better), ActorDetailViewModel.PointsDelta(0.25, 0.20));
        Assert.Equal(new Delta("−3 pts", DeltaTone.Worse), ActorDetailViewModel.PointsDelta(0.40, 0.43));
        Assert.Equal(new Delta("±0 pts"), ActorDetailViewModel.PointsDelta(0.201, 0.2));
    }

    [Fact]
    public void The_percentile_counts_this_fight_among_the_saved_ones()
    {
        Assert.Null(ActorDetailViewModel.PercentileAmong(100, []));
        Assert.Equal((67, 3), ActorDetailViewModel.PercentileAmong(200, [100, 300]));
        Assert.Equal((100, 2), ActorDetailViewModel.PercentileAmong(500, [100]));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(24, 0)]
    [InlineData(25, 25)]
    [InlineData(49, 25)]
    [InlineData(50, 50)]
    [InlineData(74, 50)]
    [InlineData(75, 75)]
    [InlineData(94, 75)]
    [InlineData(95, 95)]
    [InlineData(99, 95)]
    [InlineData(100, 100)]
    public void The_percentile_tier_uses_the_same_thresholds_as_the_history(int percentile, int tier)
    {
        Assert.Equal(tier, ActorDetailViewModel.TierOf(percentile));
        Assert.Same(HistoryItem.TierBrush(percentile), HistoryItem.TierBrush(ActorDetailViewModel.TierOf(percentile)));
    }

    [Fact]
    public void Your_kpis_compare_against_your_previous_attempt_and_show_the_percentile()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var saved = Fight(Guid.NewGuid(), null, Actor(1, "Vos", local: true, dps: 100));
        var fight = Fight(Guid.NewGuid(), null, Actor(1, "Vos", local: true, dps: 110), Actor(2, "Kaelis", dps: 150));

        // Las dos cargas terminan enseguida, como si la base respondiera al instante.
        var detail = DetailOf(fight, 1, setup: d =>
        {
            d.PastAttempts = _ => Task.FromResult<IReadOnlyList<CompareOption>>([CompareOption.Attempt(CompareOption.PreviousAttemptId, "Tu intento anterior", saved)!]);
            d.PastDps = _ => Task.FromResult<IReadOnlyList<double>>([50, 150]);
        });

        Assert.Equal(new Delta("+10% vs intento anterior", DeltaTone.Better), detail.DpsDelta);
        Assert.True(detail.HasPercentile);
        Assert.Equal(("67", 50, "entre tus 3 intentos"), (detail.Percentile, detail.PercentileTier, detail.PercentileHint));

        // Los otros jugadores primero, después tus intentos y "Sin comparar" al final.
        Assert.Equal(["Kaelis", "Tu intento anterior · 100", "Sin comparar"], detail.Others.Select(o => o.Name));
    }

    [Fact]
    public void Another_players_kpis_have_no_differences_or_percentile()
    {
        var fight = Fight(Guid.NewGuid(), null, Actor(1, "Vos", local: true), Actor(2, "Kaelis"));

        var detail = DetailOf(fight, 2, setup: d => d.PastDps = _ => Task.FromResult<IReadOnlyList<double>>([50, 150]));

        Assert.Equal(Delta.None, detail.DpsDelta);
        Assert.False(detail.HasPercentile);
    }

    // ===================== Muertes =====================

    [Fact]
    public void The_recap_adds_the_heals_from_the_log_and_ends_with_the_killing_blow()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var blow = Hit(71, 2, 88_000, HitModifiers.Critical, skill: 9102);
        var death = new DeathRecap(Start.AddSeconds(71), 2, "Tharos", [Hit(65, 2, 41_000), blow]);
        IReadOnlyList<CombatLogEntry> log =
        [
            Heal(60, 2, 9_000), // 11 s antes: fuera de la ventana
            Heal(68, 3, 7_000), // a otro jugador
            Heal(68, 2, 20_000),
            Hit(66, 2, 1_000), // los golpes ya están en el recap del motor: del registro solo se suman curaciones
            Heal(71, 2, 5_000), // en el mismo instante que el golpe final: el golpe queda último
        ];

        var rows = ActorDetailViewModel.RecapRows(death, SkillNames.Empty, log);

        Assert.Equal(["-6.0 s", "-3.0 s", "0.0 s", "0.0 s"], rows.Select(r => r.Time));
        Assert.Equal(["−41.0k", "+20.0k", "+5.0k", "−88.0k"], rows.Select(r => r.Amount));
        Assert.Equal([false, true, true, false], rows.Select(r => r.IsHeal));
        Assert.Equal([false, false, false, true], rows.Select(r => r.IsKillingBlow));
        Assert.Equal("Skill 9102 de Kromede · crítico", rows[^1].What);
        Assert.Equal("Skill 9201 de Morwen", rows[1].What);

        // Sin registro, solo los golpes.
        Assert.Equal(2, ActorDetailViewModel.RecapRows(death, SkillNames.Empty).Count);
    }

    [Fact]
    public void A_death_says_who_killed_and_with_what()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var death = new DeathRecap(Start.AddSeconds(71), 2, "Tharos", [Hit(65, 2, 41_000), Hit(71, 2, 88_000, HitModifiers.Critical, skill: 9102)]);

        var item = ActorDetailViewModel.Describe(death, Start, SkillNames.Empty);

        Assert.Equal(("01:11", "1:11"), (item.When, item.Clock));
        Assert.True(item.HasKillingBlow);
        Assert.Equal(("Kromede", "Skill 9102", "88.0k", " (crítico)"), (item.Killer, item.SkillName, item.Amount, item.FlagsText));
        Assert.Equal("Lo mató Kromede con Skill 9102 por 88.0k (crítico)", item.Summary);
        Assert.Equal("2 golpes por 129.0k en los 10 s anteriores", item.LastHits);
        Assert.Equal("ÚLTIMOS 10 SEGUNDOS", ActorDetailViewModel.RecapTitle);

        var unexplained = ActorDetailViewModel.Describe(new DeathRecap(Start.AddSeconds(5), 2, "Tharos", []), Start, SkillNames.Empty);
        Assert.False(unexplained.HasKillingBlow);
        Assert.Equal("Murió (sin golpes registrados antes)", unexplained.Summary);
        Assert.Empty(unexplained.Rows);
    }

    [Fact]
    public void The_deaths_follow_the_last_one_unless_another_is_being_viewed()
    {
        var id = Guid.NewGuid();
        EncounterSnapshot With(int count) => Fight(
            id,
            [
                .. Enumerable.Range(1, count).Select(i => new DeathRecap(Start.AddSeconds(i * 10), 2, "Tharos", [Hit(i * 10, 2, 1_000)])),
                new DeathRecap(Start.AddSeconds(5), 1, "Vos", []), // de otro jugador: no cuenta
            ],
            Actor(1, "Vos", local: true),
            Actor(2, "Tharos"));

        var detail = DetailOf(With(2), 2);

        Assert.True(detail.HasDeaths);
        Assert.Equal(1, detail.SelectedDeathIndex);
        Assert.Equal("MUERTE 2 DE 2 · 0:20", detail.DeathPosition);
        Assert.False(detail.NextDeathCommand.CanExecute(null));

        detail.PreviousDeathCommand.Execute(null);
        Assert.Equal("MUERTE 1 DE 2 · 0:10", detail.DeathPosition);
        Assert.False(detail.PreviousDeathCommand.CanExecute(null));

        // Llega otra muerte mientras se mira la primera: se queda en esa.
        detail.Update(With(3));
        Assert.Equal(3, detail.Deaths.Count);
        Assert.Equal(0, detail.SelectedDeathIndex);

        // Mirando la última, la nueva pasa a ser la que se ve.
        detail.SelectedDeathIndex = 2;
        detail.Update(With(4));
        Assert.Equal("MUERTE 4 DE 4 · 0:40", detail.DeathPosition);

        // Otra pelea: arranca en su última muerte.
        detail.Update(With(2) with { Id = Guid.NewGuid() });
        Assert.Equal("MUERTE 2 DE 2 · 0:20", detail.DeathPosition);
    }

    [Fact]
    public void A_player_without_deaths_has_nothing_to_navigate()
    {
        var detail = DetailOf(Fight(Guid.NewGuid(), null, Actor(2, "Tharos")), 2);

        Assert.False(detail.HasDeaths);
        Assert.Null(detail.SelectedDeath);
        Assert.Equal(string.Empty, detail.DeathPosition);
        Assert.False(detail.PreviousDeathCommand.CanExecute(null));
        Assert.False(detail.NextDeathCommand.CanExecute(null));
    }

    // ===================== Registro integrado =====================

    [Fact]
    public void The_deaths_tab_finds_the_death_in_the_log_with_the_fight_clock()
    {
        var death = new DeathRecap(Start.AddSeconds(71), 2, "Tharos", [Hit(65, 2, 41_000), Hit(71, 2, 88_000, HitModifiers.Critical)]);
        IReadOnlyList<CombatLogEntry> log = [.. death.LastHits, new(death.Timestamp, LogKind.Death, 2, "Tharos", 2, "Tharos", 0, 0)];
        var detail = DetailOf(Fight(Guid.NewGuid(), [death], Actor(2, "Tharos")), 2, setup: d => d.LogSource = () => log);

        // El registro se arma al abrir Muertes o Log, no con Skills.
        Assert.False(detail.HasLog);

        detail.IsDeathsTab = true;

        var lines = detail.Log;
        Assert.NotNull(lines);
        Assert.True(detail.HasLog);
        Assert.False(detail.IsLogEmpty);
        Assert.Equal(["01:05.0", "01:11.0", "01:11.0"], lines.Lines.Select(l => l.Time));
        var deathLine = detail.DeathLogLine;
        Assert.NotNull(deathLine);
        Assert.True(deathLine.IsDeath);
        Assert.Equal(death.Timestamp, deathLine.Timestamp);

        lines.Search = "zzz";
        Assert.True(detail.IsLogEmpty);
        Assert.Equal("Nada coincide con el filtro", detail.LogEmptyText);
    }

    [Fact]
    public void The_log_says_why_it_is_empty()
    {
        var fight = Fight(Guid.NewGuid(), null, Actor(2, "Tharos"));

        var withoutSource = DetailOf(fight, 2);
        withoutSource.IsLogTab = true;
        Assert.False(withoutSource.HasLog);
        Assert.True(withoutSource.IsLogEmpty);
        Assert.Equal("Sin registro de golpes para esta pelea", withoutSource.LogEmptyText);

        // El registro del motor es de otra pelea (una que terminó antes): no se mezcla.
        var otherFight = DetailOf(fight, 2, setup: d => d.LogSource = () => [Hit(-500, 2, 1_000), Hit(-400, 2, 1_000)]);
        otherFight.IsLogTab = true;
        Assert.True(otherFight.HasLog);
        Assert.True(otherFight.IsLogEmpty);
        Assert.StartsWith("Sin golpes en el registro", otherFight.LogEmptyText, StringComparison.Ordinal);
    }

    [Fact]
    public void The_log_belongs_to_a_fight_only_if_it_overlaps_it()
    {
        var fight = Fight(Guid.NewGuid(), null, Actor(2, "Tharos"));
        IReadOnlyList<CombatLogEntry> during = [Hit(50, 2, 1_000)];
        IReadOnlyList<CombatLogEntry> straddling = [Hit(-10, 2, 1_000), Hit(5, 2, 1_000)];

        Assert.Same(during, ActorDetailViewModel.ForFight(during, fight));
        Assert.Same(straddling, ActorDetailViewModel.ForFight(straddling, fight));
        Assert.Empty(ActorDetailViewModel.ForFight([Hit(-200, 2, 1_000), Hit(-150, 2, 1_000)], fight));
        Assert.Empty(ActorDetailViewModel.ForFight([Hit(101, 2, 1_000)], fight));
        Assert.Empty(ActorDetailViewModel.ForFight([], fight));
    }

    // ===================== Comparar =====================

    [Fact]
    public void Comparing_unchecks_the_tabs_and_picking_a_tab_goes_back()
    {
        var fight = Fight(Guid.NewGuid(), null, Actor(1, "Vos", local: true, dps: 90), Actor(2, "Kaelis", dps: 120));
        var detail = DetailOf(fight, 1);

        Assert.Equal(["Kaelis", "Sin comparar"], detail.Others.Select(o => o.Name));
        Assert.True(detail.Others[^1].IsNone);
        Assert.False(detail.HasComparison);
        Assert.True(detail.IsSkillsTab);

        detail.CompareWith = detail.Others[0];
        Assert.True(detail.HasComparison);
        Assert.False(detail.IsSkillsTab || detail.IsDeathsTab || detail.IsLogTab);
        Assert.Equal(("VOS", "KAELIS"), (detail.MineColumn, detail.TheirsColumn));
        Assert.Contains(detail.Comparison, r => r.Label == "DPS" && r.TheirsBetter);

        detail.IsDeathsTab = true;
        Assert.False(detail.HasComparison);
        Assert.Null(detail.CompareWith);
        Assert.True(detail.IsDeathsTab);

        detail.CompareWith = detail.Others[0];
        detail.CompareWith = detail.Others[^1];
        Assert.False(detail.HasComparison);
        Assert.Empty(detail.Comparison);
    }

    [Fact]
    public void Alone_there_is_nobody_to_compare_with()
    {
        var detail = DetailOf(Fight(Guid.NewGuid(), null, Actor(1, "Vos", local: true)), 1);

        Assert.Empty(detail.Others);
    }

    [Fact]
    public void Skills_are_compared_by_name_with_the_bar_full_at_a_quarter()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var mine = Actor(1, "Vos", skills: [Skill(1, 1_000, 10), Skill(2, 500, 5)]);
        var theirs = Actor(2, "Kaelis", skills: [Skill(1, 800, 8), Skill(2, 500, 6), Skill(3, 200, 2)]);

        var rows = ActorDetailViewModel.SkillDiff(mine, TimeSpan.FromSeconds(10), theirs, TimeSpan.FromSeconds(10), SkillNames.Empty);

        Assert.Equal(["Skill 1", "Skill 2", "Skill 3"], rows.Select(r => r.Name));
        Assert.Equal([("1.0k", "800"), ("500", "500"), ("0", "200")], rows.Select(r => (r.Mine, r.Theirs)));
        Assert.Equal([DeltaTone.Better, DeltaTone.Neutral, DeltaTone.Worse], rows.Select(r => r.Tone));
        Assert.Equal([(1.0, 0.0), (0.0, 0.0), (0.0, 1.0)], rows.Select(r => (r.Gain, r.Loss)));
        Assert.Equal(["10 / 8 (+2)", "5 / 6 (−1)", "0 / 2 (−2)"], rows.Select(r => r.Hits));

        // Un 5 % de diferencia llena un quinto de la media columna.
        var close = ActorDetailViewModel.SkillDiff(
            Actor(1, "Vos", skills: [Skill(1, 1_050, 10)]), TimeSpan.FromSeconds(10), Actor(2, "Kaelis", skills: [Skill(1, 1_000, 10)]), TimeSpan.FromSeconds(10), SkillNames.Empty);
        Assert.Equal(0.2, close[0].Gain, precision: 6);
    }

    [Fact]
    public void Variants_of_a_skill_add_up_in_the_comparison()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var names = new SkillNames(new Dictionary<uint, string> { [15030000] = "Rayo" });
        var mine = Actor(1, "Vos", skills: [Skill(15030340, 600, 6), Skill(15030341, 400, 4)]);
        var theirs = Actor(2, "Kaelis", skills: [Skill(15030340, 1_000, 10)]);

        var row = Assert.Single(ActorDetailViewModel.SkillDiff(mine, TimeSpan.FromSeconds(10), theirs, TimeSpan.FromSeconds(10), names));

        Assert.Equal(("Rayo", "1.0k", "1.0k", "10 / 10", DeltaTone.Neutral), (row.Name, row.Mine, row.Theirs, row.Hits, row.Tone));
    }

    [Fact]
    public void The_insight_points_at_the_skill_that_lost_the_most()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var mine = Actor(1, "Vos", skills: [Skill(1, 1_000, 10), Skill(2, 500, 5)]);
        var theirs = Actor(2, "Kaelis", skills: [Skill(1, 800, 8), Skill(2, 500, 6), Skill(3, 200, 2)]);

        Assert.Equal(("Perdiste más en ", "Skill 3", ": 2 golpes menos (−200)."), ActorDetailViewModel.Insight(mine, theirs, SkillNames.Empty));
        Assert.Equal("Tharos perdió más en ", ActorDetailViewModel.Insight(mine, theirs, SkillNames.Empty, "Tharos").Lead);

        // Si no perdiste en ninguna, la que más ventaja sacó.
        var ahead = Actor(1, "Vos", skills: [Skill(1, 1_000, 10)]);
        var behind = Actor(2, "Kaelis", skills: [Skill(1, 900, 9)]);
        Assert.Equal(("Sacaste más ventaja en ", "Skill 1", ": 1 golpe más (+100)."), ActorDetailViewModel.Insight(ahead, behind, SkillNames.Empty));
        Assert.Equal("Tharos sacó más ventaja en ", ActorDetailViewModel.Insight(ahead, behind, SkillNames.Empty, "Tharos").Lead);

        Assert.Equal((string.Empty, string.Empty, string.Empty), ActorDetailViewModel.Insight(mine, mine, SkillNames.Empty));
    }

    [Fact]
    public void The_compare_cards_say_when_how_and_by_how_much()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var evening = new DateTimeOffset(new DateTime(2026, 10, 16, 21, 37, 0, DateTimeKind.Local));
        Assert.Equal("HOY 21:37", ActorDetailViewModel.WhenLabel(evening, evening.AddMinutes(20)));
        Assert.Equal("12 OCT", ActorDetailViewModel.WhenLabel(new DateTimeOffset(new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Local)), evening));

        var fight = new EncounterSnapshot(Guid.NewGuid(), Start, Start.AddSeconds(252), "Kromede", true, 0, []);
        var actor = Actor(1, "Vos");
        Assert.Equal("Kromede · kill 4:12 · 3 muertes", ActorDetailViewModel.FightLine(fight, actor with { Deaths = 3 }));
        Assert.Equal("Kromede · sin kill · 4:12 · 1 muerte", ActorDetailViewModel.FightLine(fight with { TargetKilled = false }, actor with { Deaths = 1 }));
        Assert.Equal("Kromede · kill 3:00 · 0 muertes", ActorDetailViewModel.FightLine(fight with { MainTargetDuration = TimeSpan.FromSeconds(180) }, actor));

        Assert.Equal(new Delta("+4.2%", DeltaTone.Better), ActorDetailViewModel.RelativeDelta(104.2, 100));
        Assert.Equal(new Delta("−2.7%", DeltaTone.Worse), ActorDetailViewModel.RelativeDelta(97.3, 100));
        Assert.Equal(new Delta("0.0%"), ActorDetailViewModel.RelativeDelta(100.01, 100));
        Assert.Equal(Delta.None, ActorDetailViewModel.RelativeDelta(5, 0));

        Assert.Equal([10.0, 15.0, 20.0], ActorDetailViewModel.CumulativeDps([10, 20, 30]));
    }

    // ===================== Renombrar skill =====================

    [Fact]
    public void Renaming_for_all_variants_names_the_base_and_clears_the_variants()
    {
        var names = new SkillNames(new Dictionary<uint, string>()) { Custom = new Dictionary<uint, string> { [15030341] = "Viejo", [9001] = "Otra" } };
        var fight = Fight(Guid.NewGuid(), null, Actor(1, "Vos", local: true, skills: [Skill(15030340, 600, 6), Skill(15030341, 400, 4)]));
        var detail = DetailOf(fight, 1, names);
        var renamed = new List<(uint Id, string Name)>();
        detail.SkillRenamed += (id, name) =>
        {
            renamed.Add((id, name));
            names.Custom = SkillNames.With(names.Custom, id, name);
        };

        detail.StartRenameSkillCommand.Execute(detail.Skills[0]);

        Assert.True(detail.IsRenamingSkill);
        Assert.True(detail.HasRenameVariants);
        Assert.Equal("Aplicar a todas las variantes (1503xxxx)", detail.RenameVariantsText);
        Assert.Equal(string.Empty, detail.SkillNameDraft); // "Skill 15030340" no es un nombre para editar

        detail.SkillNameDraft = "Rayo";
        detail.SaveSkillNameCommand.Execute(null);

        Assert.Equal([(15030000u, "Rayo"), (15030341u, "")], renamed);
        Assert.Equal(["Rayo", "Rayo"], detail.Skills.Select(s => s.Name));
        Assert.False(detail.IsRenamingSkill);
        Assert.Equal("Otra", names.Of(9001));

        // Solo esta variante: el nombre propio pisa al de la base.
        detail.StartRenameSkillCommand.Execute(detail.Skills[1]);
        Assert.Equal("Rayo", detail.SkillNameDraft);
        detail.ApplyToVariants = false;
        detail.SkillNameDraft = "Solo esta";
        detail.SaveSkillNameCommand.Execute(null);

        Assert.Equal((15030341u, "Solo esta"), renamed[^1]);
        Assert.Equal(["Rayo", "Solo esta"], detail.Skills.Select(s => s.Name));
    }

    [Fact]
    public void Cancelling_a_rename_changes_nothing_and_skills_without_a_base_have_no_variants()
    {
        var fight = Fight(Guid.NewGuid(), null, Actor(1, "Vos", local: true, skills: [Skill(9001, 600, 6)]));
        var detail = DetailOf(fight, 1);
        var renamed = 0;
        detail.SkillRenamed += (_, _) => renamed++;

        detail.StartRenameSkillCommand.Execute(detail.Skills[0]);
        Assert.False(detail.HasRenameVariants);
        Assert.Equal(string.Empty, detail.RenameVariantsText);

        detail.SkillNameDraft = "Algo";
        detail.CancelSkillRenameCommand.Execute(null);

        Assert.False(detail.IsRenamingSkill);
        Assert.Equal(0, renamed);
        Assert.Equal("Skill 9001", detail.Skills[0].Name);
    }

    [Fact]
    public void The_rename_subtitle_shows_the_id_only_when_the_skill_has_a_name()
    {
        Assert.EndsWith("ID 15030340 · base 15030000", ActorDetailViewModel.RenameSubtitleOf(15030340, "Rayo"), StringComparison.Ordinal);
        Assert.EndsWith("base 15030000", ActorDetailViewModel.RenameSubtitleOf(15030340, "Skill 15030340"), StringComparison.Ordinal);
        Assert.DoesNotContain("ID", ActorDetailViewModel.RenameSubtitleOf(15030340, "Skill 15030340"), StringComparison.Ordinal);
        Assert.Equal(string.Empty, ActorDetailViewModel.RenameSubtitleOf(9001, "Skill 9001"));
        Assert.Equal("ID 9001", ActorDetailViewModel.RenameSubtitleOf(9001, "Golpe"));
    }

    [Fact]
    public void A_name_given_to_the_base_beats_the_variant_in_the_file()
    {
        var names = new SkillNames(new Dictionary<uint, string> { [15030341] = "Del archivo", [15040000] = "Base del archivo" })
        {
            Custom = new Dictionary<uint, string> { [15030000] = "Mío" },
        };

        Assert.Equal("Mío", names.Of(15030341));
        Assert.Equal("Mío", names.Of(15030342));
        Assert.Equal("Base del archivo", names.Of(15040007));
        Assert.Equal("Skill 9001", names.Of(9001));

        names.Custom = SkillNames.With(names.Custom, 15030341, "Exacto");
        Assert.Equal("Exacto", names.Of(15030341));

        Assert.Equal(15030000u, SkillNames.BaseOf(15030341));
        Assert.Null(SkillNames.BaseOf(15030000));
        Assert.Null(SkillNames.BaseOf(9001));
        Assert.Null(SkillNames.BaseOf(19000001));
    }

    [Fact]
    public void A_skill_row_notes_a_missing_name_and_the_dot_share()
    {
        Assert.Equal("sin nombre", SkillRowViewModel.NoteText("Skill 5", string.Empty));
        Assert.Equal("sin nombre · DoT 62%", SkillRowViewModel.NoteText("Skill 5", "DoT 62%"));
        Assert.Equal("DoT 62%", SkillRowViewModel.NoteText("Rayo", "DoT 62%"));
        Assert.Equal(string.Empty, SkillRowViewModel.NoteText("Rayo", string.Empty));

        var row = new SkillRowViewModel();
        row.Update("Skill 5", new SkillSnapshot(5, 100, 2, 0, 60, 1, PeriodicDamage: 50), 100, TimeSpan.FromSeconds(10));
        Assert.StartsWith("sin nombre · DoT", row.Note, StringComparison.Ordinal);

        row.Name = "Rayo";
        Assert.StartsWith("DoT", row.Note, StringComparison.Ordinal);
    }
}

/// <summary>Fija la cultura del hilo durante un test (los textos del VM usan CurrentCulture) y la devuelve al terminar.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public CultureScope(CultureInfo culture) => CultureInfo.CurrentCulture = culture;

    public void Dispose() => CultureInfo.CurrentCulture = _previous;
}
