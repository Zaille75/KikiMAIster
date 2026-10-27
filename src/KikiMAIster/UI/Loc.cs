using MegaCrit.Sts2.Core.Localization;

namespace KikiMAIster.UI;

/// UI strings in English and French. Follows the game language unless forced in the settings.
public static class Loc
{
    private static readonly Dictionary<string, (string En, string Fr)> Strings = new()
    {
        ["scope.Turn"] = ("Turn", "Tour"),
        ["scope.PreviousTurn"] = ("Last turn", "Tour préc."),
        ["scope.Combat"] = ("Combat", "Combat"),
        ["scope.Run"] = ("Run", "Partie"),
        ["section.damage"] = ("Damage", "Dégâts"),
        ["section.protection"] = ("Protection", "Protection"),
        ["section.debuffs"] = ("Debuffs", "Affaiblissements"),
        ["section.taken"] = ("Taken", "Subis"),
        ["section.friendly"] = ("Friendly fire", "Tirs amis"),
        ["col.prevented"] = ("Prevented", "Évités"),
        ["col.forAllies"] = ("For allies", "Pour alliés"),
        ["col.wasted"] = ("Wasted", "Gâché"),
        ["col.useless"] = ("Useless", "Inutile"),
        ["col.noDebuff"] = ("None", "Aucun"),
        ["col.petHp"] = ("Pet HP", "PV familier"),
        ["tip.col.petHp"] = ("HP given to your pet (summon, max HP, heal), never wasted", "PV donnés au familier (invocation, PV max, soins), jamais gâchés"),
        ["tip.col.prevented"] = ("Damage actually stopped: your block, your pet, relics/powers (Tungsten Rod…)", "Dégâts réellement arrêtés : ton armure, ton familier, reliques/pouvoirs (Tige de tungstène…)"),
        ["tip.col.forAllies"] = ("Damage stopped by block you gave to allies", "Dégâts arrêtés par l'armure donnée aux alliés"),
        ["tip.col.wasted"] = ("Block that stopped nothing, while a useful playable card (attack, debuff, draw…) stayed in hand", "Armure qui n'a rien arrêté, alors qu'une carte utile jouable (attaque, affaiblissement, pioche…) est restée en main"),
        ["tip.col.useless"] = ("Block that stopped nothing, with no useful card to play instead (or Ethereal, or with another effect)", "Armure qui n'a rien arrêté, sans carte utile à jouer à la place (ou Éthérée, ou avec un autre effet)"),
        ["tip.debuffXY"] = ("x/y = times applied / total stacks", "x/y = nombre d'applications / cumuls au total"),
        ["tip.dot"] = ("Damage over time (Poison, Doom…)", "Dégâts sur la durée (Poison, Malédiction…)"),
        ["tip.preventedAllies"] = ("Prevented for allies", "Évités pour les alliés"),
        ["tip.unusedAllies"] = ("…of which given to allies", "…dont donnée aux alliés"),
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
        ["tip.spent"] = ("Block spent by cards", "Armure dépensée par des cartes"),
        ["tip.pet"] = ("Absorbed by pet", "Absorbés par le familier"),
        ["tip.petDeaths"] = ("Pet deaths", "Morts du familier"),
        ["tip.byEffects"] = ("Prevented by relics/powers", "Évités par reliques/pouvoirs"),
        ["tip.healSelf"] = ("Healing (self)", "Soins (soi)"),
        ["tip.healAllies"] = ("Healing (allies)", "Soins (alliés)"),
        ["tip.fromEnemies"] = ("From enemies", "Des ennemis"),
        ["tip.fromAllies"] = ("From allies", "Des alliés"),
        ["tip.fromSelf"] = ("Self-inflicted", "Auto-infligés"),
        ["tip.other"] = ("Other", "Autres"),
        ["tip.none"] = ("Nothing yet", "Rien pour l'instant"),
        ["col.total"] = ("Total", "Total"),
        ["cat.cards"] = ("Direct", "Direct"),
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
        ["why.title"] = ("Why wasted:", "Pourquoi gâché :"),
        ["why.wasted"] = ("Round {0} · {1}: {2} wasted. In hand: {3} · block {4} · incoming attack {5} → {6} could have been played instead.",
            "Tour {0} · {1} : {2} gâché(s). En main : {3} · armure {4} · attaque annoncée {5} → {6} aurait pu être jouée à la place."),
        ["why.more"] = ("…and {0} more", "…et {0} autre(s)"),
        ["summary.button"] = ("Summary", "Résumé"),
        ["summary.tip"] = ("Combats of this run, round by round (the current one is updated live)", "Combats de cette partie, tour par tour (celui en cours est mis à jour en direct)"),
        ["summary.inProgress"] = ("in progress", "en cours"),
        ["summary.auto"] = ("Show at the end of each combat", "Afficher à la fin de chaque combat"),
        ["summary.title"] = ("Combat", "Combat"),
        ["summary.victory"] = ("Victory", "Victoire"),
        ["summary.defeat"] = ("Defeat", "Défaite"),
        ["summary.rounds"] = ("rounds", "tours"),
        ["summary.round"] = ("Round", "Tour"),
        ["summary.damage"] = ("Damage", "Dégâts"),
        ["summary.prevented"] = ("Prevented", "Évités"),
        ["summary.hpLost"] = ("HP lost", "PV perdus"),
        ["summary.debuffs"] = ("Debuff stacks", "Cumuls d'affaiblissements"),
        ["empty"] = ("No combat yet", "Aucun combat pour l'instant"),
        ["hint"] = ("F7: show/hide · drag the title to move · click a row for details", "F7 : afficher/masquer · glisser le titre pour déplacer · cliquer sur une ligne pour le détail"),
    };

    public static bool French => ModConfig.Current.Language switch
    {
        "fr" => true,
        "en" => false,
        _ => LocManager.Instance?.Language == "fra"
    };

    public static bool Has(string key) => Strings.ContainsKey(key);

    public static string T(string key) =>
        Strings.TryGetValue(key, out var s) ? (French ? s.Fr : s.En) : key;
}
