using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Update;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// Actualización automática del zip portable: qué versión toma de los releases de GitHub, cómo la baja y la deja lista,
/// y cómo la instala al arrancar sin tocar la carpeta datos ni la marca del modo portable.
/// </summary>
public sealed class UpdateTests
{
    private const string Download = "https://github.com/Yaizsk0/HysteriaMeter/releases/download/";

    [Theory]
    [InlineData("0.16.0-alpha.1", 0, 16, 0, "alpha.1")]
    [InlineData("v0.16.0", 0, 16, 0, "")]
    [InlineData("0.15.0+5f3c2a1", 0, 15, 0, "")]
    [InlineData("1.2.3-beta.2+5f3c2a1", 1, 2, 3, "beta.2")]
    public void Parses_tags_and_informational_versions(string text, int major, int minor, int patch, string prerelease)
    {
        Assert.Equal(new ReleaseVersion(major, minor, patch, prerelease), ReleaseVersion.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.16")]
    [InlineData("0.16.0.1")]
    [InlineData("0.16.x")]
    [InlineData("0.16.0-")]
    [InlineData("0.16.0-alpha..1")]
    [InlineData("0.16.0-alpha_1")]
    [InlineData("-1.0.0")]
    [InlineData("nightly")]
    public void Rejects_what_is_not_a_version(string? text) => Assert.Null(ReleaseVersion.Parse(text));

    [Fact]
    public void Orders_versions_like_semver()
    {
        string[] ordered =
        [
            "0.15.0", "0.16.0-alpha", "0.16.0-alpha.1", "0.16.0-alpha.2", "0.16.0-alpha.10", "0.16.0-alpha.beta",
            "0.16.0-beta.2", "0.16.0-beta.11", "0.16.0-rc.1", "0.16.0", "0.16.1", "0.17.0", "1.0.0",
        ];
        var versions = ordered.Select(v => ReleaseVersion.Parse(v)!).ToArray();

        for (var i = 1; i < versions.Length; i++)
        {
            Assert.True(versions[i].IsNewerThan(versions[i - 1]), $"{ordered[i]} tendría que ser posterior a {ordered[i - 1]}");
            Assert.False(versions[i - 1].IsNewerThan(versions[i]));
        }

        // El commit que suma el SDK a la versión informativa no cuenta.
        Assert.Equal(0, ReleaseVersion.Compare(ReleaseVersion.Parse("0.16.0+5f3c2a1")!, ReleaseVersion.Parse("v0.16.0")!));
    }

    [Theory]
    [InlineData("v0.16.0-alpha.1+5f3c2a1", "0.16.0-alpha.1")]
    [InlineData("0.16.0", "0.16.0")]
    public void Prints_without_the_v_or_the_commit(string text, string expected) =>
        Assert.Equal(expected, ReleaseVersion.Parse(text)!.ToString());

    [Fact]
    public void Takes_the_newest_release_with_zip_and_checksum()
    {
        var offer = ReleaseFeed.Newest(Feed(Release("v0.16.0-alpha.2"), Release("v0.16.0-alpha.3"), Release("v0.15.0")), Version("0.16.0-alpha.1"));

        Assert.NotNull(offer);
        Assert.Equal(Version("0.16.0-alpha.3"), offer.Version);
        Assert.Equal(new Uri(Download + "v0.16.0-alpha.3/HysteriaMeter-0.16.0-alpha.3-portable-win-x64.zip"), offer.Zip);
        Assert.Equal(new Uri(Download + "v0.16.0-alpha.3/HysteriaMeter-0.16.0-alpha.3-portable-win-x64.zip.sha256"), offer.Checksum);
        Assert.Equal(74_000_000L, offer.ZipSize);
    }

    [Fact]
    public void Skips_drafts()
    {
        var offer = ReleaseFeed.Newest(Feed(Release("v0.16.0-alpha.3", draft: true), Release("v0.16.0-alpha.2")), Version("0.16.0-alpha.1"));

        Assert.Equal(Version("0.16.0-alpha.2"), offer?.Version);
    }

    [Fact]
    public void A_stable_version_does_not_move_to_a_test_one()
    {
        var feed = Feed(Release("v0.17.0-alpha.1"), Release("v0.16.2", prerelease: true), Release("v0.16.1"));

        Assert.Equal(Version("0.16.1"), ReleaseFeed.Newest(feed, Version("0.16.0"))?.Version);
    }

    [Fact]
    public void A_test_version_moves_on_to_the_stable_one()
    {
        var offer = ReleaseFeed.Newest(Feed(Release("v0.16.0")), Version("0.16.0-alpha.3"));

        Assert.Equal(Version("0.16.0"), offer?.Version);
    }

    [Fact]
    public void Offers_nothing_without_a_newer_complete_release_from_this_repo()
    {
        var current = Version("0.16.0-alpha.2");

        Assert.Null(ReleaseFeed.Newest(Feed(Release("v0.16.0-alpha.2"), Release("v0.16.0-alpha.1")), current));
        Assert.Null(ReleaseFeed.Newest(Feed(Release("v0.16.0-alpha.3", checksum: false)), current));
        Assert.Null(ReleaseFeed.Newest(Feed(Release("v0.16.0-alpha.3", host: "https://example.com/Yaizsk0/HysteriaMeter/releases/download/")), current));
        Assert.Null(ReleaseFeed.Newest(Feed(Release("version-rara")), current));
        Assert.Null(ReleaseFeed.Newest("{\"message\":\"API rate limit exceeded\"}", current));
        Assert.Null(ReleaseFeed.Newest("<html>", current));
    }

    [Theory]
    [InlineData("A0488EB172CC37DDD2A18D6AC975D22C0F7D6B30E4F26B6DC867BF885544A180  HysteriaMeter.zip\n", "A0488EB172CC37DDD2A18D6AC975D22C0F7D6B30E4F26B6DC867BF885544A180")]
    [InlineData("a0488eb172cc37ddd2a18d6ac975d22c0f7d6b30e4f26b6dc867bf885544a180", "a0488eb172cc37ddd2a18d6ac975d22c0f7d6b30e4f26b6dc867bf885544a180")]
    [InlineData("", null)]
    [InlineData("a0488eb172cc37ddd2a18d6ac975d22c0f7d6b30e4f26b6dc867bf885544a18  corto.zip", null)]
    [InlineData("z0488eb172cc37ddd2a18d6ac975d22c0f7d6b30e4f26b6dc867bf885544a180  rara.zip", null)]
    public void Reads_the_hash_of_the_checksum_file(string text, string? expected) =>
        Assert.Equal(expected, Updater.ParseChecksum(text));

    [Fact]
    public async Task Stages_the_zip_when_the_hash_matches()
    {
        using var folders = new Folders();
        using var server = new FakeServer();
        using var http = new HttpClient(server, disposeHandler: false);
        var offer = server.Publish("0.16.0-alpha.2", Zip(("HysteriaMeter.exe", "nuevo"), ("data/opcodes.json", "{}")));
        var updater = folders.CreateUpdater();

        Assert.True(await updater.StageAsync(http, offer, CancellationToken.None));

        Assert.Equal(Version("0.16.0-alpha.2"), updater.Staged());
        Assert.Equal("nuevo", folders.Read("datos/actualizacion/nuevo/HysteriaMeter.exe"));
        Assert.False(File.Exists(Path.Combine(folders.Staging, "descarga.zip")));

        // Ya está bajada: no la vuelve a pedir.
        var requests = server.Requests.Count;
        Assert.False(await updater.StageAsync(http, offer, CancellationToken.None));
        Assert.Equal(requests, server.Requests.Count);
    }

    [Fact]
    public async Task A_zip_that_does_not_match_its_hash_is_not_staged()
    {
        using var folders = new Folders();
        using var server = new FakeServer();
        using var http = new HttpClient(server, disposeHandler: false);
        var offer = server.Publish("0.16.0-alpha.2", Zip(("HysteriaMeter.exe", "nuevo")), checksumOf: Zip(("HysteriaMeter.exe", "otro")));
        var updater = folders.CreateUpdater();

        Assert.False(await updater.StageAsync(http, offer, CancellationToken.None));

        Assert.Null(updater.Staged());
        Assert.False(Directory.Exists(Path.Combine(folders.Staging, "nuevo")));
        Assert.False(File.Exists(Path.Combine(folders.Staging, "descarga.zip")));
    }

    [Fact]
    public async Task A_zip_without_the_exe_is_not_staged()
    {
        using var folders = new Folders();
        using var server = new FakeServer();
        using var http = new HttpClient(server, disposeHandler: false);
        var offer = server.Publish("0.16.0-alpha.2", Zip(("LEEME.txt", "sin programa")));
        var updater = folders.CreateUpdater();

        Assert.False(await updater.StageAsync(http, offer, CancellationToken.None));

        Assert.Null(updater.Staged());
        Assert.False(Directory.Exists(Path.Combine(folders.Staging, "nuevo")));
    }

    [Fact]
    public async Task A_zip_over_the_limit_is_not_even_requested()
    {
        using var folders = new Folders();
        using var server = new FakeServer();
        using var http = new HttpClient(server, disposeHandler: false);
        var offer = server.Publish("0.16.0-alpha.2", Zip(("HysteriaMeter.exe", "nuevo"))) with { ZipSize = Updater.MaxZipBytes + 1 };

        Assert.False(await folders.CreateUpdater().StageAsync(http, offer, CancellationToken.None));

        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Installs_the_program_without_touching_the_user_data()
    {
        using var folders = new Folders();
        folders.Write("HysteriaMeter.exe", "viejo");
        folders.Write("data/opcodes.json", "viejo");
        folders.Write("portable.txt", "marca");
        folders.Write("datos/preferencias.json", "mías");
        var updater = await folders.StageAsync(
            "0.16.0-alpha.2",
            ("HysteriaMeter.exe", "nuevo"),
            ("data/opcodes.json", "nuevo"),
            ("data/npcs.json", "nuevo"),
            ("portable.txt", "otra"),
            ("datos/preferencias.json", "pisadas"));

        Assert.True(updater.ApplyStaged(Version("0.16.0-alpha.1")));

        Assert.Equal("nuevo", folders.Read("HysteriaMeter.exe"));
        Assert.Equal("nuevo", folders.Read("data/opcodes.json"));
        Assert.Equal("nuevo", folders.Read("data/npcs.json"));
        Assert.Equal("marca", folders.Read("portable.txt"));
        Assert.Equal("mías", folders.Read("datos/preferencias.json"));
        Assert.Equal("viejo", folders.Read("HysteriaMeter.exe.old"));
        Assert.Null(updater.Staged());

        updater.CleanupLeftovers();

        Assert.False(File.Exists(folders.In("HysteriaMeter.exe.old")));
        Assert.False(File.Exists(folders.In("data/opcodes.json.old")));
        Assert.False(File.Exists(Path.Combine(folders.Staging, "viejos.txt")));
    }

    [Fact]
    public async Task A_version_that_is_not_newer_is_discarded_without_installing()
    {
        using var folders = new Folders();
        folders.Write("HysteriaMeter.exe", "viejo");
        var updater = await folders.StageAsync("0.16.0-alpha.2", ("HysteriaMeter.exe", "nuevo"));

        Assert.False(updater.ApplyStaged(Version("0.16.0-alpha.2")));

        Assert.Equal("viejo", folders.Read("HysteriaMeter.exe"));
        Assert.Null(updater.Staged());
    }

    [Fact]
    public async Task A_file_in_use_rolls_back_everything_and_keeps_the_update_for_later()
    {
        using var folders = new Folders();
        folders.Write("HysteriaMeter.exe", "viejo");
        folders.Write("data/a.json", "viejo");
        folders.Write("data/opcodes.json", "viejo");
        var updater = await folders.StageAsync(
            "0.16.0-alpha.2",
            ("HysteriaMeter.exe", "nuevo"),
            ("data/a.json", "nuevo"),
            ("data/opcodes.json", "nuevo"));

        using (new FileStream(folders.In("data/opcodes.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(updater.ApplyStaged(Version("0.16.0-alpha.1")));
        }

        Assert.Equal("viejo", folders.Read("HysteriaMeter.exe"));
        Assert.Equal("viejo", folders.Read("data/a.json"));
        Assert.Equal("viejo", folders.Read("data/opcodes.json"));
        Assert.Empty(Directory.EnumerateFiles(folders.Program, "*.old", SearchOption.AllDirectories));
        Assert.Equal(Version("0.16.0-alpha.2"), updater.Staged());

        // Ya sin el archivo tomado, el próximo arranque la instala entera.
        Assert.True(updater.ApplyStaged(Version("0.16.0-alpha.1")));
        Assert.Equal("nuevo", folders.Read("data/opcodes.json"));
    }

    [Fact]
    public async Task Cleanup_only_deletes_listed_leftovers_inside_the_program_folder()
    {
        using var folders = new Folders();
        folders.Write("HysteriaMeter.exe", "viejo");
        folders.Write("otro.old", "no es de la actualización");
        folders.Write("../afuera.old", "fuera del programa");
        var updater = await folders.StageAsync("0.16.0-alpha.2", ("HysteriaMeter.exe", "nuevo"));
        Assert.True(updater.ApplyStaged(Version("0.16.0-alpha.1")));
        folders.AddLeftovers(@"..\afuera.old", "HysteriaMeter.exe");

        // El .exe viejo sigue abierto (el meter anterior todavía no cerró): queda para la próxima limpieza.
        using (new FileStream(folders.In("HysteriaMeter.exe.old"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            updater.CleanupLeftovers();
        }

        Assert.True(File.Exists(folders.In("HysteriaMeter.exe.old")));
        updater.CleanupLeftovers();

        Assert.False(File.Exists(folders.In("HysteriaMeter.exe.old")));
        Assert.Equal("nuevo", folders.Read("HysteriaMeter.exe"));
        Assert.True(File.Exists(folders.In("otro.old")));
        Assert.True(File.Exists(folders.In("../afuera.old")));
    }

    [Fact]
    public void Restart_arguments_carry_the_pid_to_wait_for()
    {
        var (rest, pid) = AppRestart.Split(["--modo", AppRestart.WaitArgument, "4321", "--otro"]);
        Assert.Equal("--modo --otro", string.Join(' ', rest));
        Assert.Equal(4321, pid);

        (rest, pid) = AppRestart.Split(["--modo"]);
        Assert.Equal("--modo", string.Join(' ', rest));
        Assert.Null(pid);

        // Un pid que no es número se descarta con su argumento; el argumento solo, sin pid, queda como está.
        (rest, pid) = AppRestart.Split([AppRestart.WaitArgument, "rara"]);
        Assert.Empty(rest);
        Assert.Null(pid);
        (rest, pid) = AppRestart.Split([AppRestart.WaitArgument]);
        Assert.Equal(AppRestart.WaitArgument, Assert.Single(rest));
        Assert.Null(pid);
    }

    [Fact]
    public void Tray_and_settings_show_the_staged_version()
    {
        Assert.Equal(string.Empty, TrayIcon.UpdateText(null));
        Assert.Equal("Reiniciar con la versión 0.16.0-alpha.2", TrayIcon.UpdateText(Version("0.16.0-alpha.2")));

        var state = new UpdateState();
        Assert.Equal(string.Empty, state.StatusText);
        state.Staged = Version("0.16.0-alpha.2");
        Assert.StartsWith("La versión 0.16.0-alpha.2 ya está bajada", state.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_local_build_never_updates_itself()
    {
        // Este build no pasó por packaging/portable.ps1 (-p:ReleaseBuild=true).
        Assert.False(UpdateState.IsReleaseBuild);
        Assert.False(UpdateState.IsAvailable);
    }

    [Fact]
    public void Auto_update_is_on_by_default_and_a_profile_does_not_change_it()
    {
        Assert.True(new MeterPreferences().AutoUpdate);
        // Mismas opciones que UserStore: un preferencias.json de antes de esta opción queda con la actualización prendida.
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        Assert.True(JsonSerializer.Deserialize<MeterPreferences>("{}", options)!.AutoUpdate);

        var json = ProfileFile.ToJson(new MeterPreferences { AutoUpdate = true });
        var imported = ProfileFile.FromJson(json, new MeterPreferences { AutoUpdate = false });

        Assert.False(imported?.AutoUpdate);
    }

    private static ReleaseVersion Version(string text) => ReleaseVersion.Parse(text)!;

    private static string Feed(params object[] releases) => JsonSerializer.Serialize(releases);

    /// <summary>Un release como lo devuelve la API de GitHub, con los assets que sube el workflow de release.</summary>
    private static object Release(string tag, bool prerelease = false, bool draft = false, bool checksum = true, string host = Download)
    {
        var zip = ReleaseFeed.ZipName(Version(tag));
        var assets = new List<object> { new { name = zip, size = 74_000_000L, browser_download_url = $"{host}{tag}/{zip}" } };
        if (checksum)
        {
            assets.Add(new { name = zip + ".sha256", size = 112L, browser_download_url = $"{host}{tag}/{zip}.sha256" });
        }

        return new { tag_name = tag, draft, prerelease, assets };
    }

    private static byte[] Zip(params (string Path, string Text)[] entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }

        return memory.ToArray();
    }

    /// <summary>Una carpeta del programa con su <c>datos\actualizacion</c>, que se borra al terminar.</summary>
    private sealed class Folders : IDisposable
    {
        public Folders()
        {
            Root = Path.Combine(Path.GetTempPath(), $"hysteria-actualizacion-{Guid.NewGuid():N}");
            Program = Path.Combine(Root, "programa");
            Staging = Path.Combine(Program, "datos", "actualizacion");
            Directory.CreateDirectory(Program);
        }

        public string Root { get; }

        public string Program { get; }

        public string Staging { get; }

        public Updater CreateUpdater() => new(Program, Staging);

        public string In(string relative) => Path.Combine(Program, relative);

        public void Write(string relative, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(In(relative))!);
            File.WriteAllText(In(relative), text);
        }

        public string Read(string relative) => File.ReadAllText(In(relative));

        /// <summary>Suma renglones a la lista de .old que dejó la instalación (<c>viejos.txt</c>).</summary>
        public void AddLeftovers(params string[] lines) => File.AppendAllLines(Path.Combine(Staging, "viejos.txt"), lines);

        /// <summary>Deja lista una versión con estos archivos, bajándola como lo hace el meter.</summary>
        public async Task<Updater> StageAsync(string version, params (string Path, string Text)[] entries)
        {
            using var server = new FakeServer();
            using var http = new HttpClient(server, disposeHandler: false);
            var updater = CreateUpdater();
            Assert.True(await updater.StageAsync(http, server.Publish(version, Zip(entries)), CancellationToken.None));
            return updater;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Hace de GitHub: sirve el zip y el .sha256 de cada versión publicada.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files = [];

        public List<Uri> Requests { get; } = [];

        public UpdateOffer Publish(string version, byte[] zip, byte[]? checksumOf = null)
        {
            var parsed = Version(version);
            var name = ReleaseFeed.ZipName(parsed);
            var zipUri = new Uri($"{Download}v{parsed}/{name}");
            var checksumUri = new Uri($"{Download}v{parsed}/{name}.sha256");
            var hash = Convert.ToHexStringLower(SHA256.HashData(checksumOf ?? zip));
            _files[zipUri.AbsoluteUri] = zip;
            _files[checksumUri.AbsoluteUri] = Encoding.UTF8.GetBytes($"{hash}  {name}\n");
            return new UpdateOffer(parsed, zipUri, zip.LongLength, checksumUri);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var response = _files.TryGetValue(request.RequestUri!.AbsoluteUri, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }
}
