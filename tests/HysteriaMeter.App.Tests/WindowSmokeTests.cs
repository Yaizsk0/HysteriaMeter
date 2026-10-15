using System.Runtime.ExceptionServices;
using System.Windows;
using HysteriaMeter.App.Detail;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// Carga las ventanas con sus plantillas expandidas: un recurso que falta o un binding mal escrito en XAML
/// compila igual y recién explota al abrir la app, así que esto lo atrapa en el CI.
/// </summary>
public sealed class WindowSmokeTests
{
    [Fact]
    public void Overlay_and_detail_windows_load_with_rows()
    {
        RunOnSta(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = Application.Current as App ?? new App();
            app.InitializeComponent();

            var viewModel = new MeterViewModel(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences(), _ => { });
            var row = new ActorRowViewModel();
            row.Update(1, Actor(), MeterTab.Damage, top: 3000, total: 3000, TimeSpan.FromSeconds(10));
            viewModel.Actors.Add(row);
            viewModel.Detail.Select(row);
            viewModel.History.Add(new HistoryItem("x", "Boss", "15/10 21:42", "02:47", "3.0k", Killed: true));
            viewModel.IsHistoryOpen = true;
            viewModel.IsViewingHistory = true;
            viewModel.Detail.Update(new EncounterSnapshot(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(10), "Boss", false, 3000, [Actor()]));

            Layout(new OverlayWindow(viewModel));
            Layout(new DetailWindow(viewModel.Detail));
            Assert.Equal(2, viewModel.Detail.Skills.Count);
        });
    }

    private static ActorSnapshot Actor() => new(
        Id: 7, Name: "Tester", ClassId: null, IsLocalPlayer: true, Damage: 3000, Dps: 300, Share: 1, Hits: 3, Crits: 1, MaxHit: 1500, Healing: 0,
        Skills:
        [
            new SkillSnapshot(100, 2000, 2, 1, 1500, 0.667),
            new SkillSnapshot(200, 1000, 1, 0, 1000, 0.333, PeriodicDamage: 400),
        ]);

    /// <summary>Mide y acomoda sin mostrar la ventana: alcanza para expandir plantillas y resolver recursos.</summary>
    private static void Layout(Window window)
    {
        if (window.Content is FrameworkElement content)
        {
            content.Measure(new Size(800, 600));
            content.Arrange(new Rect(0, 0, 800, 600));
            content.UpdateLayout();
        }

        window.Close();
    }

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
