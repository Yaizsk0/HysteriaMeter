using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HysteriaMeter.App.Icons;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// La descarga de íconos de MetaBot.GG, sin red: un servidor falso hace de MetaBot y un conversor falso reemplaza al
/// códec WebP de Windows (en los runners de CI no está).
/// </summary>
public sealed class IconDownloaderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Directory.CreateTempSubdirectory().FullName;

    private static readonly IconManifest Manifest = new([11010000, 12060000], new Dictionary<int, string> { [15] = "sorcerer" });

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Downloads_the_missing_icons_as_png_with_the_credit()
    {
        var server = new FakeMetaBot().Serve(IconManifest.SkillUrl(11010000)).Serve(IconManifest.SkillUrl(12060000)).Serve(IconManifest.ClassUrl("sorcerer"));

        var result = await Downloader(server).RunAsync(Manifest, _folder, Now, CancellationToken.None);

        Assert.Equal(new IconResult(IconOutcome.Done, 3, 0, 3), result);
        Assert.Equal("png:RIFF0000WEBPhttps://metabot.gg/web/aion2/skills/11010000.webp", File.ReadAllText(Path.Combine(_folder, "iconos", "11010000.png")));
        Assert.Equal("png:RIFF0000WEBPhttps://metabot.gg/web/aion2/classes/sorcerer.webp", File.ReadAllText(Path.Combine(_folder, "clases", "15.png")));
        Assert.Contains("metabot.gg/en/aion-2/skills", File.ReadAllText(Path.Combine(_folder, "iconos", "FUENTE.txt")), StringComparison.Ordinal);
        Assert.Contains("metabot.gg/en/aion-2/classes", File.ReadAllText(Path.Combine(_folder, "clases", "FUENTE.txt")), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Keeps_the_icons_already_on_disk_and_does_not_ask_for_them()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "iconos"));
        File.WriteAllText(Path.Combine(_folder, "iconos", "11010000.png"), "a mano");
        var server = new FakeMetaBot().Serve(IconManifest.SkillUrl(11010000)).Serve(IconManifest.SkillUrl(12060000)).Serve(IconManifest.ClassUrl("sorcerer"));

        var result = await Downloader(server).RunAsync(Manifest, _folder, Now, CancellationToken.None);

        Assert.Equal(2, result.Downloaded);
        Assert.Equal("a mano", File.ReadAllText(Path.Combine(_folder, "iconos", "11010000.png")));
        Assert.DoesNotContain(IconManifest.SkillUrl(11010000), server.Requests);

        var again = await Downloader(server).RunAsync(Manifest, _folder, Now, CancellationToken.None);

        Assert.Equal(new IconResult(IconOutcome.UpToDate, 0, 0, 3), again);
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task A_missing_icon_is_not_asked_again_until_a_week_later()
    {
        var server = new FakeMetaBot().Serve(IconManifest.SkillUrl(11010000)).Serve(IconManifest.ClassUrl("sorcerer"));

        var first = await Downloader(server).RunAsync(Manifest, _folder, Now, CancellationToken.None);
        var nextDay = await Downloader(server).RunAsync(Manifest, _folder, Now.AddDays(1), CancellationToken.None);
        server.Serve(IconManifest.SkillUrl(12060000));
        var nextWeek = await Downloader(server).RunAsync(Manifest, _folder, Now.AddDays(8), CancellationToken.None);

        Assert.Equal(new IconResult(IconOutcome.Done, 2, 1, 2), first);
        Assert.Equal(new IconResult(IconOutcome.UpToDate, 0, 0, 2), nextDay);
        Assert.Equal(new IconResult(IconOutcome.Done, 1, 0, 3), nextWeek);
        Assert.Equal(2, server.Requests.Count(u => u == IconManifest.SkillUrl(12060000)));
        Assert.False(File.Exists(Path.Combine(_folder, "metabot.json")));
    }

    [Fact]
    public async Task Without_the_webp_codec_it_asks_for_nothing()
    {
        var server = new FakeMetaBot().Serve(IconManifest.SkillUrl(11010000)).Serve(IconManifest.SkillUrl(12060000)).Serve(IconManifest.ClassUrl("sorcerer"));
        var downloader = new IconDownloader(new HttpClient(server), _ => throw new NotSupportedException()) { Pause = TimeSpan.Zero };

        var result = await downloader.RunAsync(Manifest, _folder, Now, CancellationToken.None);

        Assert.Equal(new IconResult(IconOutcome.CodecMissing, 0, 0, 0), result);
        Assert.Empty(server.Requests);
        Assert.False(Directory.Exists(Path.Combine(_folder, "iconos")));
    }

    [Fact]
    public async Task A_page_that_is_not_an_image_counts_as_missing_and_not_as_a_missing_codec()
    {
        var server = new FakeMetaBot().ServeXml(IconManifest.SkillUrl(11010000)).Serve(IconManifest.SkillUrl(12060000)).Serve(IconManifest.ClassUrl("sorcerer"));
        var downloader = new IconDownloader(new HttpClient(server), webp => IconDownloader.IsWebp(webp) ? [1] : throw new NotSupportedException()) { Pause = TimeSpan.Zero };

        var result = await downloader.RunAsync(Manifest, _folder, Now, CancellationToken.None);

        Assert.Equal(new IconResult(IconOutcome.Done, 2, 1, 2), result);
        Assert.False(IconDownloader.IsWebp("<?xml ver"u8));
        Assert.True(IconDownloader.IsWebp("RIFF\u0000\u0000\u0000\u0000WEBPVP8 "u8));
    }

    [Fact]
    public async Task With_the_codec_an_image_windows_cannot_open_is_skipped_and_the_rest_keeps_going()
    {
        var server = new FakeMetaBot().Serve(IconManifest.SkillUrl(11010000)).Serve(IconManifest.SkillUrl(12060000)).Serve(IconManifest.ClassUrl("sorcerer"));
        var downloader = new IconDownloader(
            new HttpClient(server),
            webp => System.Text.Encoding.UTF8.GetString(webp).EndsWith("11010000.webp", StringComparison.Ordinal) ? throw new NotSupportedException() : [1])
        {
            Pause = TimeSpan.Zero,
        };

        var result = await downloader.RunAsync(Manifest, _folder, Now, CancellationToken.None);

        Assert.Equal(new IconResult(IconOutcome.Done, 2, 1, 2), result);
        Assert.True(File.Exists(Path.Combine(_folder, "metabot.json")));
    }

    [Fact]
    public async Task Several_rejections_in_a_row_are_a_block_and_are_not_written_down_as_missing()
    {
        var many = new IconManifest([11010000, 11020000, 11050000, 11080000, 11100000, 11110000, 11130000], new Dictionary<int, string>());
        var server = new FakeMetaBot();

        var result = await Downloader(server).RunAsync(many, _folder, Now, CancellationToken.None);

        Assert.Equal(new IconResult(IconOutcome.Blocked, 0, 0, 0), result);
        Assert.Equal(IconDownloader.BlockedAfter, server.Requests.Count);
        Assert.False(File.Exists(Path.Combine(_folder, "metabot.json")));

        // Uno que llega en el medio corta la racha: los rechazos sueltos sí son íconos que faltan.
        server.Serve(IconManifest.SkillUrl(11100000));
        var again = await Downloader(server).RunAsync(many, _folder, Now, CancellationToken.None);

        Assert.Equal(new IconResult(IconOutcome.Done, 1, 6, 1), again);
    }

    [Fact]
    public async Task Without_connection_it_keeps_what_it_got_for_the_next_start()
    {
        var server = new FakeMetaBot().Serve(IconManifest.SkillUrl(11010000));
        server.FailAfter = 1;

        var result = await Downloader(server).RunAsync(Manifest, _folder, Now, CancellationToken.None);

        Assert.Equal(new IconResult(IconOutcome.Offline, 1, 0, 1), result);
        Assert.False(File.Exists(Path.Combine(_folder, "metabot.json")));
    }

    [Fact]
    public async Task A_new_icon_shows_up_in_a_table_that_had_looked_for_it_before()
    {
        var names = SkillNames.Empty.WithIcons(Path.Combine(_folder, "iconos"));
        Assert.Null(names.IconOf(12060240));
        var server = new FakeMetaBot().Serve(IconManifest.SkillUrl(11010000)).Serve(IconManifest.SkillUrl(12060000)).Serve(IconManifest.ClassUrl("sorcerer"));

        await Downloader(server).RunAsync(Manifest, _folder, Now, CancellationToken.None);

        Assert.Equal(Path.Combine(_folder, "iconos", "12060000.png"), names.IconOf(12060240));
    }

    [Fact]
    public void Converts_with_the_windows_decoder()
    {
        // PNG de 2x1 hecho con WIC: el códec PNG está en todos los Windows; el WebP se probó a mano contra MetaBot.
        var source = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 1, 2, 3, 255, 4, 5, 6, 128 }, 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);

        var png = IconDownloader.WebpToPng(stream.ToArray());

        var frame = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var pixels = new byte[8];
        new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, 8, 0);
        Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 5, 6, 128 }, pixels);
        Assert.Throws<NotSupportedException>(() => IconDownloader.WebpToPng([0x42, 0x41, 0x44, 0x21, 0, 0, 0, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public void The_published_manifest_lists_the_metabot_skills_and_the_eight_classes()
    {
        var manifest = IconManifest.Load(FindRepoFile("iconos-metabot.json"));

        Assert.Equal(279, manifest.Skills.Count);
        Assert.Contains(11010000u, manifest.Skills);
        Assert.Contains(18010000u, manifest.Skills);
        Assert.Equal(["gladiator", "templar", "assassin", "ranger", "sorcerer", "spiritmaster", "cleric", "chanter"], Enumerable.Range(11, 8).Select(id => manifest.Classes[id]));
        Assert.Equal(IconManifest.Empty, IconManifest.Parse("{ roto"));
    }

    [Fact]
    public void The_status_says_what_happened()
    {
        Assert.Equal(string.Empty, IconState.Describe(false, null));
        Assert.StartsWith("Bajando", IconState.Describe(true, null), StringComparison.Ordinal);
        Assert.Contains("WebP", IconState.Describe(false, new IconResult(IconOutcome.CodecMissing, 0, 0, 0)), StringComparison.Ordinal);
        Assert.Equal("Se bajaron 287 imágenes de MetaBot.GG (287 en disco).", IconState.Describe(false, new IconResult(IconOutcome.Done, 287, 0, 287)));
        Assert.Equal("Íconos al día (286 imágenes en disco). MetaBot no tiene 1: se vuelve a probar en una semana.", IconState.Describe(false, new IconResult(IconOutcome.UpToDate, 0, 1, 286)));
        Assert.StartsWith("MetaBot.GG rechazó", IconState.Describe(false, new IconResult(IconOutcome.Blocked, 0, 0, 0)), StringComparison.Ordinal);
    }

    private static IconDownloader Downloader(FakeMetaBot server) =>
        new(new HttpClient(server), webp => System.Text.Encoding.UTF8.GetBytes("png:" + System.Text.Encoding.UTF8.GetString(webp))) { Pause = TimeSpan.Zero };

    private static string FindRepoFile(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(name);
    }

    /// <summary>Hace de MetaBot: cada imagen publicada responde una firma WebP y su URL; el resto, 404.</summary>
    private sealed class FakeMetaBot : HttpMessageHandler
    {
        private readonly HashSet<string> _served = [];

        public List<Uri> Requests { get; } = [];

        /// <summary>Después de tantos pedidos, la conexión se cae.</summary>
        public int FailAfter { get; set; } = int.MaxValue;

        public FakeMetaBot Serve(Uri url)
        {
            _served.Add(url.AbsoluteUri);
            return this;
        }

        private readonly HashSet<string> _xml = [];

        /// <summary>Responde 200 con una página de error en vez de la imagen.</summary>
        public FakeMetaBot ServeXml(Uri url)
        {
            _xml.Add(url.AbsoluteUri);
            return Serve(url);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Requests.Count >= FailAfter)
            {
                throw new HttpRequestException("sin conexión");
            }

            Requests.Add(request.RequestUri!);
            var url = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(_served.Contains(url)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent((_xml.Contains(url) ? "<?xml version=\"1.0\"?><Error/>" : "RIFF0000WEBP") + url) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
