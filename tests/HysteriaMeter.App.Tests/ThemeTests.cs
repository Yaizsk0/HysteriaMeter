using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// El tema del rediseño (Themes/Theme.xaml) y los temas alternativos: fuentes embebidas, colores que el código repite,
/// las claves derivadas del acento y el repintado en vivo. Cargan Theme.xaml solo, sin crear la App (WPF admite una
/// por proceso y la usan WindowSmokeTests).
/// </summary>
public sealed class ThemeTests
{
    private static readonly string[] Themed =
    [
        "WindowBg", "Panel", "PanelRaised", "PanelSolid", "Surface", "CardBg", "InputBg", "MenuBg",
        "Gold", "GoldBright", "Text", "TextBright", "TextMuted", "TextDim",
    ];

    private static readonly string[] AccentDerived =
        ["GoldDim", "AccentFaint", "AccentSoft", "AccentTint", "AccentLine", "AccentStrong", "RowBorderLocal", "NameLocal"];

    [Fact]
    public void Fonts_resolve_to_the_embedded_files()
    {
        RunOnSta(() =>
        {
            var theme = LoadTheme();
            AssertFamily("Barlow Semi Condensed", (FontFamily)theme["NumberFont"]);
            AssertFamily("Barlow Semi Condensed", (FontFamily)theme["TitleFont"]);
            AssertFamily("Inter", (FontFamily)theme["BodyFont"]);
            Assert.All(MeterPreferences.BundledFonts, name => AssertFamily(name, MeterViewModel.FontOf(name)));
        });
    }

    [Fact]
    public void Colors_repeated_in_code_match_theme_xaml()
    {
        RunOnSta(() =>
        {
            var theme = LoadTheme();
            Color Key(string key) => ((SolidColorBrush)theme[key]).Color;

            // Percentiles: HistoryItem.TierBrush repite los Percentile* con los mismos umbrales.
            foreach (var (percentile, key) in new[]
            {
                (0, "Percentile0"), (24, "Percentile0"), (25, "Percentile25"), (50, "Percentile50"),
                (75, "Percentile75"), (95, "Percentile95"), (100, "Percentile100"),
            })
            {
                Assert.Equal(Key(key), ColorOf(HistoryItem.TierBrush(percentile)));
            }

            // Modo dungeon: kill, wipe y trash de la línea de tiempo, y el color de la clase desconocida.
            var entered = new DateTimeOffset(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);
            var player = new ActorSnapshot(1, "Yo", null, true, 1000, 10, 1, 10, 1, 100, 0, [], MainTargetDamage: 1000, MainTargetDps: 10);
            EncounterSnapshot Fight(int start, bool killed) => new(
                Guid.NewGuid(), entered.AddSeconds(start), entered.AddSeconds(start + 100), "Kromede", killed, 1000, [player],
                TargetIsBoss: true, TargetNpcId: 5);
            // Trash: un bicho común que muere en 10 s.
            var trash = new EncounterSnapshot(
                Guid.NewGuid(), entered, entered.AddSeconds(10), "Bicho", true, 1000, [player], TargetIsBoss: false, TargetNpcId: 9);
            var view = new DungeonSummaryViewModel(DungeonSummary.From([trash, Fight(60, false), Fight(400, true)], entered));

            var fills = view.Timeline.Select(s => ColorOf(s.Fill)).ToList();
            Assert.Contains(Key("Wipe"), fills);
            Assert.Contains(Key("Kill"), fills);
            Assert.Contains(Key("Neutral"), fills);
            Assert.Equal(Key("Kill"), ColorOf(Assert.Single(view.Cards).ResultBrush));
            Assert.Equal(Key("ClassUnknown"), ColorOf(Assert.Single(view.Rows).Color));

            // El jugador local, hasta que App aplica el tema.
            Assert.Equal(Key("Gold"), PlayerColors.DefaultAccent);
        });
    }

    [Fact]
    public void Default_theme_keeps_theme_xaml_and_the_others_follow_their_accent()
    {
        RunOnSta(() =>
        {
            var defaults = MeterThemes.Snapshot(LoadTheme());
            Assert.Equal(Themed.Concat(AccentDerived).Order(), defaults.Keys.Order());

            // "Dorado" usa Theme.xaml tal cual, también una derivada que no tenga el RGB del acento.
            var custom = new Dictionary<string, Color>(defaults) { ["NameLocal"] = Color.FromRgb(0xF0, 0xC8, 0x78) };
            Assert.Equal(custom, MeterThemes.Resolve(MeterThemes.Default, custom));
            Assert.Equal(defaults, MeterThemes.Resolve(MeterThemes.Default, defaults));

            foreach (var theme in MeterThemes.All.Skip(1))
            {
                var palette = MeterThemes.Resolve(theme.Name, defaults);
                var accent = theme.Colors["Gold"];
                Assert.Equal(defaults.Keys.Order(), palette.Keys.Order());
                Assert.All(Themed, key => Assert.Equal(theme.Colors[key], palette[key]));
                Assert.All(AccentDerived, key => Assert.Equal(Color.FromArgb(defaults[key].A, accent.R, accent.G, accent.B), palette[key]));
            }
        });
    }

    [Fact]
    public void Dynamic_references_repaint_when_the_theme_changes()
    {
        RunOnSta(() =>
        {
            var theme = LoadTheme();
            var defaults = MeterThemes.Snapshot(theme);
            // Como en la App: Theme.xaml va como diccionario combinado y el tema escribe en el de arriba.
            var border = new Border { Resources = new ResourceDictionary { MergedDictionaries = { theme } } };
            border.SetResourceReference(Border.BackgroundProperty, "Panel");
            border.SetResourceReference(Border.BorderBrushProperty, "GoldDim");
            Assert.Equal(defaults["Panel"], ColorOf(border.Background));

            var applied = MeterThemes.Apply(border.Resources, "Carmesí", defaults);
            Assert.Equal(applied["Panel"], ColorOf(border.Background));
            Assert.Equal(applied["GoldDim"], ColorOf(border.BorderBrush));
            Assert.NotEqual(defaults["Panel"], applied["Panel"]);

            MeterThemes.Apply(border.Resources, MeterThemes.Default, defaults);
            Assert.Equal(defaults["Panel"], ColorOf(border.Background));
            Assert.Equal(defaults["GoldDim"], ColorOf(border.BorderBrush));
        });
    }

    [Fact]
    public void Local_player_takes_the_theme_accent()
    {
        var accent = MeterThemes.Of("Abismo").Colors["Gold"];
        var me = new ActorSnapshot(7, "Tester", null, true, 3000, 300, 1, 3, 1, 1500, 0, []);
        var row = new ActorRowViewModel();
        try
        {
            PlayerColors.UseAccent(accent);
            row.Update(1, me, MeterTab.Damage, 3000, 3000, TimeSpan.FromSeconds(10));
            Assert.Equal(accent, ColorOf(row.Accent));
            Assert.Equal(Color.FromArgb(0x55, accent.R, accent.G, accent.B), ColorOf(row.BarFill));
        }
        finally
        {
            PlayerColors.UseAccent(PlayerColors.DefaultAccent);
        }

        row.Update(1, me, MeterTab.Damage, 3000, 3000, TimeSpan.FromSeconds(10));
        Assert.Equal(PlayerColors.DefaultAccent, ColorOf(row.Accent));
    }

    [Fact]
    public void Importing_a_profile_touches_theme_and_font()
    {
        Assert.True(MeterSession.Touches([string.Empty], nameof(MeterViewModel.SelectedTheme)));
        Assert.True(MeterSession.Touches([string.Empty], nameof(MeterViewModel.FontName)));
        Assert.True(MeterSession.Touches([nameof(MeterViewModel.SelectedTheme)], nameof(MeterViewModel.SelectedTheme)));
        Assert.False(MeterSession.Touches([nameof(MeterViewModel.FontName)], nameof(MeterViewModel.SelectedTheme)));
        Assert.False(MeterSession.Touches([], nameof(MeterViewModel.FontName)));
    }

    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;

    private static void AssertFamily(string family, FontFamily font)
    {
        var typeface = new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Assert.True(typeface.TryGetGlyphTypeface(out var glyphs), $"{font.Source} no resolvió a un archivo de fuente");
        Assert.Contains(family, glyphs.FamilyNames.Values);
        // Embebida: el archivo sale del ensamblado de la app, no de la carpeta de fuentes de Windows.
        Assert.Equal("pack", glyphs.FontUri.Scheme);
    }

    private static ResourceDictionary LoadTheme() =>
        (ResourceDictionary)Application.LoadComponent(new Uri("/HysteriaMeter;component/Themes/Theme.xaml", UriKind.Relative));

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
