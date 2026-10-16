using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

// hysteria-extract: lee los archivos del AION 2 instalado (con el juego cerrado) y deja lo extraído
// en %LOCALAPPDATA%\HysteriaMeter\Juego. Nada de esto va al repo (constitución, principio VII).
//
//   hysteria-extract info    --juego <carpeta> [--clave 0x...]
//   hysteria-extract listar  --juego <carpeta> --clave 0x... [--filtro texto]
//   hysteria-extract exportar --juego <carpeta> --clave 0x... --filtro texto
//
// La clave también se puede pasar con la variable de entorno AION2_AES_KEY.
if (args.Length == 0 || args[0] is "-h" or "--help" or "ayuda")
{
    Console.WriteLine("""
        Uso:
          hysteria-extract info     --juego <carpeta de AION2> [--clave 0x...]
          hysteria-extract listar   --juego <carpeta de AION2> --clave 0x... [--filtro texto]
          hysteria-extract exportar --juego <carpeta de AION2> --clave 0x... --filtro texto

        La clave también se puede pasar con la variable de entorno AION2_AES_KEY.
        Lo exportado queda en %LOCALAPPDATA%\HysteriaMeter\Juego (nunca en el repo).
        Cerrá el juego antes: la herramienta solo lee los archivos en disco.
        """);
    return 0;
}

var command = args[0];
var options = ParseOptions(args.Skip(1).ToArray());
if (!options.TryGetValue("juego", out var gameDir))
{
    Console.Error.WriteLine("Falta --juego <carpeta de AION2>.");
    return 2;
}

var paks = Directory.Exists(Path.Combine(gameDir, "Aion2", "Content", "Paks")) ? Path.Combine(gameDir, "Aion2", "Content", "Paks") : gameDir;
var key = options.GetValueOrDefault("clave") ?? Environment.GetEnvironmentVariable("AION2_AES_KEY");
var filter = options.GetValueOrDefault("filtro");
var output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HysteriaMeter", "Juego");

using var provider = new DefaultFileProvider(paks, SearchOption.AllDirectories, new VersionContainer(EGame.GAME_Aion2), StringComparer.OrdinalIgnoreCase);
provider.Initialize();
if (key is not null)
{
    provider.SubmitKey(new FGuid(), new FAesKey(key));
}
else
{
    provider.Mount();
}

Console.WriteLine($"Contenedores: {provider.MountedVfs.Count} abiertos, {provider.UnloadedVfs.Count} cifrados sin abrir. Archivos visibles: {provider.Files.Count}.");
if (command == "info")
{
    if (key is null && provider.UnloadedVfs.Count > 0)
    {
        Console.WriteLine("Los contenedores cifrados necesitan la clave (--clave o AION2_AES_KEY).");
    }

    return 0;
}

if (provider.Files.Count == 0)
{
    Console.Error.WriteLine("No se pudo abrir ningún archivo: revisá la clave.");
    return 1;
}

var files = provider.Files.Keys
    .Where(k => filter is null || k.Contains(filter, StringComparison.OrdinalIgnoreCase))
    .Order(StringComparer.OrdinalIgnoreCase)
    .ToList();

switch (command)
{
    case "listar":
        foreach (var file in files)
        {
            Console.WriteLine(file);
        }

        Console.WriteLine($"{files.Count} archivos.");
        return 0;
    case "exportar" when filter is null:
        Console.Error.WriteLine("Exportar todo son decenas de GB: pasá --filtro.");
        return 2;
    case "exportar":
        var exported = 0;
        foreach (var file in files)
        {
            try
            {
                Export(provider, file, output);
                exported++;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"{file}: {e.Message}");
            }
        }

        Console.WriteLine($"{exported} de {files.Count} archivos exportados en {output}.");
        return 0;
    default:
        Console.Error.WriteLine($"Comando desconocido: {command}.");
        return 2;
}

// Los paquetes (.uasset) se guardan como JSON legible; el resto (por ejemplo .dat), tal cual.
static void Export(DefaultFileProvider provider, string file, string output)
{
    var target = Path.Combine(output, file.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    if (file.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
    {
        var exports = provider.LoadPackage(file).GetExports();
        File.WriteAllText(Path.ChangeExtension(target, ".json"), JsonConvert.SerializeObject(exports, Formatting.Indented));
    }
    else if (!file.EndsWith(".uexp", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".ubulk", StringComparison.OrdinalIgnoreCase))
    {
        File.WriteAllBytes(target, provider.SaveAsset(file));
    }
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i + 1 < args.Length; i += 2)
    {
        options[args[i].TrimStart('-')] = args[i + 1];
    }

    return options;
}
