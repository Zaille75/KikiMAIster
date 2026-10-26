using MegaCrit.Sts2.Core.Localization;

namespace KikiMAIster.UI;

/// UI strings in English and French. Follows the game language unless forced in the settings.
public static class Loc
{
    private static readonly Dictionary<string, (string En, string Fr)> Strings = new()
    {
        ["scope.Turn"] = ("Turn", "Tour"),
        ["scope.Combat"] = ("Combat", "Combat"),
        ["scope.Run"] = ("Run", "Partie"),
        ["section.damage"] = ("Damage", "Dégâts"),
        ["section.protection"] = ("Protection", "Protection"),
        ["section.debuffs"] = ("Debuffs", "Affaiblissements"),
        ["section.taken"] = ("Taken", "Subis"),
        ["section.friendly"] = ("Friendly fire", "Tirs amis"),
        ["col.prevented"] = ("Prevented", "Évités"),
        ["col.forAllies"] = ("For allies", "Pour alliés"),
        ["col.wastedUseless"] = ("Wasted/Useless", "Gâché/Inutile"),
        ["col.applied"] = ("Applied", "Appliqués"),
        ["col.stacks"] = ("Stacks", "Cumuls"),
        ["col.dot"] = ("DoT dmg", "Dégâts DoT"),
        ["col.hpLost"] = ("HP lost", "PV perdus"),
        ["col.blocked"] = ("Blocked", "Bloqués"),
        ["col.debuffs"] = ("Debuffs", "Affaibl."),
        ["col.toAllies"] = ("To allies", "Aux alliés"),
        ["col.self"] = ("Self", "Soi-même"),
        ["tip.bySource"] = ("Damage by source", "Dégâts par source"),
        ["tip.intoBlock"] = ("Into enemy block", "Dans l'armure ennemie"),
        ["tip.overkill"] = ("Overkill", "Excédent"),
        ["tip.kills"] = ("Kills", "Victimes"),
        ["tip.biggestHit"] = ("Biggest hit", "Plus gros coup"),
        ["tip.blockSelf"] = ("Block gained (self)", "Armure gagnée (soi)"),
        ["tip.blockAllies"] = ("Block given to allies", "Armure donnée aux alliés"),
        ["tip.preventedOwn"] = ("Prevented by own block", "Évités par sa propre armure"),
        ["tip.wasted"] = ("Wasted block", "Armure gâchée"),
        ["tip.useless"] = ("Useless help", "Aide inutile"),
        ["tip.spent"] = ("Block spent by cards", "Armure dépensée par des cartes"),
        ["tip.pet"] = ("Absorbed by pet", "Absorbés par le familier"),
        ["tip.petDeaths"] = ("Pet deaths", "Morts du familier"),
        ["tip.healSelf"] = ("Healing (self)", "Soins (soi)"),
        ["tip.healAllies"] = ("Healing (allies)", "Soins (alliés)"),
        ["tip.fromEnemies"] = ("From enemies", "Des ennemis"),
        ["tip.fromAllies"] = ("From allies", "Des alliés"),
        ["tip.fromSelf"] = ("Self-inflicted", "Auto-infligés"),
        ["tip.other"] = ("Other", "Autres"),
        ["tip.none"] = ("Nothing yet", "Rien pour l'instant"),
        ["col.total"] = ("Total", "Total"),
        ["col.friendlyTotal"] = ("Damage to allies and self", "Dégâts aux alliés et à soi"),
        ["cat.cards"] = ("Cards", "Cartes"),
        ["cat.powers"] = ("Powers", "Pouvoirs"),
        ["cat.pet"] = ("Pet", "Familier"),
        ["cat.orbs"] = ("Orbs", "Orbes"),
        ["cat.relics"] = ("Relics", "Reliques"),
        ["cat.potions"] = ("Potions", "Potions"),
        ["cat.other"] = ("Other", "Autre"),
        ["src.pet"] = ("Pet", "Familier"),
        ["src.unknown"] = ("Other", "Autre"),
        ["lang.tip"] = ("Language: click to switch English / French", "Langue : cliquer pour passer en anglais / français"),
        ["row.tip"] = ("Click to show or hide the details", "Cliquer pour afficher ou masquer le détail"),
        ["empty"] = ("No combat yet", "Aucun combat pour l'instant"),
        ["hint"] = ("F7: show/hide · drag the title to move · click a row for details", "F7 : afficher/masquer · glisser le titre pour déplacer · cliquer sur une ligne pour le détail"),
    };

    public static bool French => ModConfig.Current.Language switch
    {
        "fr" => true,
        "en" => false,
        _ => LocManager.Instance?.Language == "fra"
    };

    public static string T(string key) =>
        Strings.TryGetValue(key, out var s) ? (French ? s.Fr : s.En) : key;
}
