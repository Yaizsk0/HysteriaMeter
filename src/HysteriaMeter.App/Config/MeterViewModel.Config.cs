using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using HysteriaMeter.App.Config;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

// Lo que suma la ventana de configuración (Config/ConfigWindow) al view model del overlay: los rangos de los sliders,
// el máximo de jugadores con "Todos", el atajo para cambiar de pestaña, la placa de red, los datos del juego y la vista
// previa de los temas. Las opciones de siempre siguen en MeterViewModel.cs.
public sealed partial class MeterViewModel
{
    /// <summary>Rango del slider "Alto de fila" (de a 2 px). El modo compacto sigue usando 22.</summary>
    public const int MinRowHeight = 22;

    public const int MaxRowHeight = 40;

    /// <summary>Rango del slider "Máximo de jugadores"; "Todos" (<see cref="MaxRows"/> = 0) va aparte.</summary>
    public const int MinRowLimit = 4;

    public const int MaxRowLimit = 24;

    /// <summary>Con qué límite arranca el slider al apagar "Todos" si nunca se movió.</summary>
    private const int DefaultRowLimit = 8;

    private int _lastRowLimit = DefaultRowLimit;
    private IReadOnlyList<CaptureDeviceOption>? _captureDevices;
    private GameDataSummary? _gameData;

    /// <summary>
    /// Valor del slider "Máximo de jugadores". Con "Todos" muestra el último límite y no escribe nada: así el mínimo del
    /// slider (4) nunca pisa el 0 guardado.
    /// </summary>
    public int RowLimit
    {
        get => MaxRows == 0 ? _lastRowLimit : Math.Clamp(MaxRows, MinRowLimit, MaxRowLimit);
        set
        {
            if (MaxRows == 0)
            {
                return;
            }

            _lastRowLimit = Math.Clamp(value, MinRowLimit, MaxRowLimit);
            if (_lastRowLimit != MaxRows)
            {
                MaxRows = _lastRowLimit;
            }
        }
    }

    /// <summary>"Todos": sin máximo de jugadores (<see cref="MaxRows"/> = 0). Apagado vuelve al último límite.</summary>
    public bool ShowAllRows
    {
        get => MaxRows == 0;
        set
        {
            if (value == ShowAllRows)
            {
                return;
            }

            if (value)
            {
                _lastRowLimit = RowLimit;
            }

            MaxRows = value ? 0 : _lastRowLimit;
        }
    }

    public string HotkeyTab
    {
        get => Preferences.HotkeyTab;
        set => SetHotkey(value, Preferences.HotkeyTab, text => Preferences with { HotkeyTab = text }, nameof(HotkeyTab));
    }

    /// <summary>Atajo "Cambiar pestaña": DPS → CURA → RECIB. → DPS.</summary>
    [RelayCommand]
    private void CycleTab() => Tab = Tab switch
    {
        MeterTab.Damage => MeterTab.Healing,
        MeterTab.Healing => MeterTab.Taken,
        _ => MeterTab.Damage,
    };

    /// <summary>
    /// "Automática" y las placas de red que ve Npcap. Se piden de nuevo al abrir la configuración, pero SharpPcap guarda
    /// su lista por proceso: una placa que aparece con el meter abierto puede no verse hasta reiniciarlo.
    /// </summary>
    public IReadOnlyList<CaptureDeviceOption> CaptureDeviceOptions =>
        _captureDevices ??= CaptureDevices.Options(CaptureDevices.Source(), Preferences.CaptureDevice);

    /// <summary>Placa de red elegida. Se guarda enseguida, pero la captura la toma al reiniciar el meter.</summary>
    public CaptureDeviceOption SelectedCaptureDevice
    {
        get => CaptureDeviceOptions.FirstOrDefault(o => o.Name == Preferences.CaptureDevice) ?? CaptureDeviceOptions[0];
        set => Update(Preferences with { CaptureDevice = value?.Name ?? string.Empty }, nameof(SelectedCaptureDevice), nameof(CaptureRestartPending));
    }

    /// <summary>La placa elegida no es la que usa la captura ahora: hace falta reiniciar el meter.</summary>
    public bool CaptureRestartPending => Preferences.CaptureDevice != CaptureDevices.Applied;

    /// <summary>Lo que hay en %LOCALAPPDATA%\HysteriaMeter\Juego (o en la carpeta portable); se cuenta al abrir la configuración.</summary>
    public GameDataSummary GameData => _gameData ??= GameDataSummary.Read(AppPaths.In("Juego"));

    /// <summary>Tarjetas del selector de tema, con la vista previa de cada paleta.</summary>
#pragma warning disable CA1822 // propiedad de instancia para el binding de WPF
    public IReadOnlyList<ThemeCard> ThemeCards => ThemeCard.All();
#pragma warning restore CA1822

    [RelayCommand]
    private void OpenGameData()
    {
        Directory.CreateDirectory(GameData.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{GameData.Folder}\"") { UseShellExecute = true });
    }

    /// <summary>Al abrir la configuración se vuelven a leer las placas y la carpeta del juego: pueden haber cambiado.</summary>
    partial void OnIsSettingsOpenChanged(bool value)
    {
        if (!value)
        {
            return;
        }

        _captureDevices = null;
        _gameData = null;
        OnPropertyChanged(nameof(CaptureDeviceOptions));
        OnPropertyChanged(nameof(SelectedCaptureDevice));
        OnPropertyChanged(nameof(GameData));
    }
}
