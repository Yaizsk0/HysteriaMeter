using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Una clase del juego: nombre y símbolo (dibujado por nosotros, sin assets del juego).</summary>
/// <param name="Color">Color de la clase para las barras (opción "Colores por clase").</param>
public sealed record PlayerClass(string Name, Geometry Icon, Color Color);

/// <summary>
/// Clases por número (los IDs de skill de cada clase empiezan con él: 12010350 es de la clase 12). Los nombres y
/// el símbolo se pueden corregir en <c>data/classes.json</c> sin recompilar.
/// </summary>
public static class PlayerClasses
{
    /// <summary>Símbolos simples en un cuadro de 16×16.</summary>
    public static IReadOnlyDictionary<string, Geometry> Icons { get; } = new Dictionary<string, Geometry>(StringComparer.OrdinalIgnoreCase)
    {
        ["espada"] = Icon("M8,0.5 L9.6,9 L8,10.6 L6.4,9 Z M4.5,10 L11.5,10 L11.5,11.4 L4.5,11.4 Z M7.3,11.4 L8.7,11.4 L8.7,15.5 L7.3,15.5 Z"),
        ["escudo"] = Icon("M8,0.5 L14.5,2.8 L13.6,9 C12.8,12.2 10.6,14.4 8,15.5 C5.4,14.4 3.2,12.2 2.4,9 L1.5,2.8 Z"),
        ["dagas"] = Icon("M2.5,1 L4.5,1 L13,12.5 L11.8,13.6 Z M11.5,1 L13.5,1 L4.2,13.6 L3,12.5 Z M10.5,12.5 L13.5,15.5 L14.5,14.5 L11.5,11.5 Z M5.5,12.5 L2.5,15.5 L1.5,14.5 L4.5,11.5 Z"),
        ["arco"] = Icon("M4,0.5 C13,3.5 13,12.5 4,15.5 L4,14 C10.8,11.4 10.8,4.6 4,2 Z M4,1.2 L5,1.2 L5,14.8 L4,14.8 Z M1,7.4 L13,7.4 L13,6 L15.5,8 L13,10 L13,8.6 L1,8.6 Z"),
        ["llama"] = Icon("M8,0.5 C11.5,4.5 13.5,7 12.6,11 C12,13.8 10.2,15.5 8,15.5 C5.8,15.5 4,13.8 3.4,11 C2.8,7.8 5.4,6.6 6.6,3.6 C7.8,5.8 9,7 8.6,9.4 C10.4,8.2 10.4,5 8,0.5 Z"),
        ["orbe"] = Icon("M8,1 A7,7 0 1 1 7.99,1 Z M8,4.5 A3.5,3.5 0 1 0 8.01,4.5 Z"),
        ["cruz"] = Icon("M6.4,0.5 L9.6,0.5 L9.6,4.6 L13.5,4.6 L13.5,7.8 L9.6,7.8 L9.6,15.5 L6.4,15.5 L6.4,7.8 L2.5,7.8 L2.5,4.6 L6.4,4.6 Z"),
        ["nota"] = Icon("M10,0.5 L12.2,0.5 L12.2,11.5 C12.2,13.6 10.6,15.2 8.5,15.2 C6.9,15.2 5.8,14.1 5.8,12.8 C5.8,11.4 7.2,10.3 8.8,10.3 C9.3,10.3 9.7,10.4 10,10.6 Z M12.2,0.5 C13.6,2.2 15,3 15,5.4 C14.2,4.2 13.2,3.8 12.2,3.8 Z"),
    };

    /// <summary>Orden clásico de AION; si el número de alguna está mal, se corrige en data/classes.json.</summary>
    public static IReadOnlyDictionary<int, PlayerClass> Defaults { get; } = new Dictionary<int, PlayerClass>
    {
        [11] = new("Gladiador", Icons["espada"], Color.FromRgb(0xD8, 0x9A, 0x4E)),
        [12] = new("Templario", Icons["escudo"], Color.FromRgb(0x5A, 0x8F, 0xD8)),
        [13] = new("Asesino", Icons["dagas"], Color.FromRgb(0x9A, 0x62, 0xD8)),
        [14] = new("Ranger", Icons["arco"], Color.FromRgb(0x6C, 0xBF, 0x4F)),
        [15] = new("Hechicero", Icons["llama"], Color.FromRgb(0xE0, 0x5E, 0x4E)),
        [16] = new("Espiritualista", Icons["orbe"], Color.FromRgb(0xC0, 0x5C, 0xD6)),
        [17] = new("Clérigo", Icons["cruz"], Color.FromRgb(0xE8, 0xE0, 0xB0)),
        [18] = new("Chanter", Icons["nota"], Color.FromRgb(0x3F, 0xB8, 0x9F)),
    };

    /// <summary>La tabla en uso: la de <c>data/classes.json</c> si se cargó, si no la de por defecto.</summary>
    public static IReadOnlyDictionary<int, PlayerClass> Current { get; private set; } = Defaults;

    /// <summary>Carpeta con emblemas de clase (<c>&lt;id&gt;.png</c>), por ejemplo <c>Juego\clases</c>; <c>null</c> si no hay.</summary>
    public static string? EmblemFolder { get; set; }

    /// <summary>Ruta del emblema de la clase; <c>null</c> si no hay (queda el símbolo dibujado).</summary>
    public static string? EmblemOf(int? classId) =>
        classId is { } id && EmblemFolder is { } folder && Path.Combine(folder, $"{id}.png") is var path && File.Exists(path) ? path : null;

    public static PlayerClass? Of(int? classId) => classId is { } id && Current.TryGetValue(id, out var playerClass) ? playerClass : null;

    public static void Load(string path)
    {
        if (File.Exists(path))
        {
            Current = Parse(File.ReadAllText(path));
        }
    }

    /// <summary>
    /// <c>{ "classes": { "12": { "name": "Templario", "icon": "escudo", "color": "#5A8FD8" } } }</c>. Lo que falte o esté mal queda como
    /// en <see cref="Defaults"/>.
    /// </summary>
    public static IReadOnlyDictionary<int, PlayerClass> Parse(string json)
    {
        var table = new Dictionary<int, PlayerClass>(Defaults);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("classes", out var classes)
                || classes.ValueKind != JsonValueKind.Object)
            {
                return table;
            }

            foreach (var entry in classes.EnumerateObject())
            {
                if (!int.TryParse(entry.Name, out var id) || entry.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = entry.Value.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } text
                    ? text.Trim()
                    : table.GetValueOrDefault(id)?.Name;
                var icon = entry.Value.TryGetProperty("icon", out var i) && i.ValueKind == JsonValueKind.String && i.GetString() is { } key && Icons.TryGetValue(key, out var geometry)
                    ? geometry
                    : table.GetValueOrDefault(id)?.Icon;
                var color = entry.Value.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String && ParseColor(c.GetString()) is { } parsed
                    ? parsed
                    : table.GetValueOrDefault(id)?.Color ?? Colors.Gray;
                if (name is not null && icon is not null)
                {
                    table[id] = new PlayerClass(name, icon, color);
                }
            }
        }
        catch (JsonException)
        {
            // Un classes.json mal escrito no impide abrir el meter: quedan los nombres por defecto.
        }

        return table;
    }

    /// <summary>"#RRGGBB"; <c>null</c> si no es un color.</summary>
    public static Color? ParseColor(string? text)
    {
        if (text is not { Length: 7 } || text[0] != '#'
            || !uint.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb))
        {
            return null;
        }

        return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    private static Geometry Icon(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
