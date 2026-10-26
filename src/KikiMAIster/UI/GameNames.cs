using System.Globalization;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace KikiMAIster.UI;

/// Turns stat keys ("Card:STRIKE_SILENT", "POISON_POWER"…) into the names the game shows, in the game language.
public static class GameNames
{
    private static readonly Dictionary<string, string> Cache = new();
    private static readonly Dictionary<string, Type> Types = new();

    /// Remembers which game model an id comes from, so its real title can be used (some models, like
    /// Enfeebling Touch's power, take their title from another model instead of the "<ID>.title" entry).
    public static void Register(string id, Type modelType) => Types.TryAdd(id, modelType);
    private static string _cacheLanguage = "";

    public static string Of(string key)
    {
        var language = LocManager.Instance?.Language ?? "";
        if (language != _cacheLanguage)
        {
            Cache.Clear();
            _cacheLanguage = language;
        }
        if (Cache.TryGetValue(key, out var name)) return name;
        name = Resolve(key);
        Cache[key] = name;
        return name;
    }

    private static string Resolve(string key)
    {
        // A stat loss from a temporary power: "Force − (Toucher affaiblissant)".
        var at = key.IndexOf('@');
        if (at > 0) return $"{Of(key[..at])} ({Of(key[(at + 1)..])})";
        // A buff applied with a negative amount (Strength down…): "Force −".
        if (key.EndsWith("~DOWN")) return Of(key[..^5]) + " −";
        if (key == "Pet") return Loc.T("src.pet");
        if (key == "Unknown") return Loc.T("src.unknown");
        var sep = key.IndexOf(':');
        var kind = sep < 0 ? "" : key[..sep];
        var id = sep < 0 ? key : key[(sep + 1)..];
        if (Types.TryGetValue(id, out var type) && ModelTitle(type) is { } title) return title;
        var tables = kind switch
        {
            "Card" => new[] { "cards" },
            "Power" or "DamageOverTime" => new[] { "powers" },
            "Potion" => new[] { "potions" },
            "Relic" => new[] { "relics" },
            "Orb" => new[] { "orbs" },
            _ => new[] { "powers", "cards" }
        };
        foreach (var table in tables)
        {
            try
            {
                var text = LocString.GetIfExists(table, id + ".title")?.GetRawText();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            catch (Exception)
            {
                // Missing table or entry: fall back to a readable id.
            }
        }
        return Pretty(id);
    }

    private static string? ModelTitle(Type type)
    {
        try
        {
            var model = ModelDb.GetByIdOrNull<AbstractModel>(ModelDb.GetId(type));
            var title = model switch
            {
                CardModel c => c.Title,
                PowerModel p => p.Title.GetFormattedText(),
                RelicModel r => r.Title.GetFormattedText(),
                PotionModel p => p.Title.GetFormattedText(),
                OrbModel o => o.Title.GetFormattedText(),
                _ => null
            };
            return string.IsNullOrWhiteSpace(title) ? null : title;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// "STRIKE_IRONCLAD" → "Strike Ironclad", "POISON_POWER" → "Poison".
    private static string Pretty(string id)
    {
        if (id.EndsWith("_POWER")) id = id[..^6];
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('_', ' ').ToLowerInvariant());
    }
}
