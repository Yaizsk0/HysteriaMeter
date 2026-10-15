using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Una clase del juego: nombre y símbolo (dibujado por nosotros, sin assets del juego).</summary>
public sealed record PlayerClass(string Name, Geometry Icon);

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
        [11] = new("Gladiador", Icons["espada"]),
        [12] = new("Templario", Icons["escudo"]),
        [13] = new("Asesino", Icons["dagas"]),
        [14] = new("Ranger", Icons["arco"]),
        [15] = new("Hechicero", Icons["llama"]),
        [16] = new("Espiritualista", Icons["orbe"]),
        [17] = new("Clérigo", Icons["cruz"]),
        [18] = new("Chanter", Icons["nota"]),
    };

    /// <summary>La tabla en uso: la de <c>data/classes.json</c> si se cargó, si no la de por defecto.</summary>
    public static IReadOnlyDictionary<int, PlayerClass> Current { get; private set; } = Defaults;

    public static PlayerClass? Of(int? classId) => classId is { } id && Current.TryGetValue(id, out var playerClass) ? playerClass : null;

    public static void Load(string path)
    {
        if (File.Exists(path))
        {
            Current = Parse(File.ReadAllText(path));
        }
    }

    /// <summary>
    /// <c>{ "classes": { "12": { "name": "Templario", "icon": "escudo" } } }</c>. Lo que falte o esté mal queda como
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
                if (name is not null && icon is not null)
                {
                    table[id] = new PlayerClass(name, icon);
                }
            }
        }
        catch (JsonException)
        {
            // Un classes.json mal escrito no impide abrir el meter: quedan los nombres por defecto.
        }

        return table;
    }

    private static Geometry Icon(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
