using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HysteriaMeter.App.Update;

/// <summary>
/// Si este .exe se actualiza solo y qué versión nueva tiene lista, para la configuración y la bandeja. Solo el zip
/// portable de los releases de GitHub se actualiza: un build local nunca se reemplaza.
/// </summary>
public sealed partial class UpdateState : ObservableObject
{
    public static UpdateState Current { get; } = new();

    /// <summary>Marca que pone el csproj con <c>-p:ReleaseBuild=true</c> (la pasa <c>packaging/portable.ps1</c>).</summary>
    public static bool IsReleaseBuild { get; } = typeof(UpdateState).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(a => a.Key == "ReleaseBuild" && a.Value == "true");

    /// <summary>La versión que está corriendo, de la versión informativa ("0.16.0-alpha.1+&lt;sha&gt;").</summary>
    public static ReleaseVersion? RunningVersion { get; } = ReleaseVersion.Parse(
        typeof(UpdateState).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>Build de release, en modo portable y con versión conocida.</summary>
    public static bool IsAvailable { get; } = IsReleaseBuild && AppPaths.IsPortable && RunningVersion is not null;

    public static Updater CreateUpdater() => new(AppContext.BaseDirectory, AppPaths.In("actualizacion"));

    /// <summary>La versión ya bajada, que se instala al volver a abrir el meter.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial ReleaseVersion? Staged { get; set; }

    /// <summary>Para Configuración → PROGRAMA; vacío si no hay nada bajado.</summary>
    public string StatusText => Staged is { } version
        ? $"La versión {version} ya está bajada: se instala la próxima vez que abras el meter."
        : string.Empty;
}
