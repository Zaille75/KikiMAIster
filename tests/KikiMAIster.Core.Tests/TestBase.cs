using KikiMAIster.Core;

namespace KikiMAIster.Core.Tests;

/// Three players A, B, C, a pet owned by A and two enemies.
public abstract class TestBase
{
    protected const ulong IdA = 1, IdB = 2, IdC = 3;
    protected static readonly Actor A = Actor.Player(IdA, 1);
    protected static readonly Actor B = Actor.Player(IdB, 2);
    protected static readonly Actor C = Actor.Player(IdC, 3);
    protected static readonly Actor PetA = Actor.Pet(IdA, 10);
    protected static readonly Actor Enemy = Actor.Enemy(100);
    protected static readonly Actor Enemy2 = Actor.Enemy(101);

    protected readonly StatsEngine Engine = new();

    protected TestBase()
    {
        Engine.Handle(new RunStarted());
        Engine.Handle(new CombatStarted(new[]
        {
            new PlayerInfo(IdA, "A", "IRONCLAD"), new PlayerInfo(IdB, "B", "SILENT"), new PlayerInfo(IdC, "C", "DEFECT")
        }));
        Engine.Handle(new RoundStarted(1));
    }

    protected PlayerStats Combat(ulong id) => Engine.Get(Scope.Combat, id);

    protected void Block(Actor receiver, Actor giver, int amount) =>
        Engine.Handle(new BlockGained(receiver, giver, amount, Source.Card("DEFEND")));

    /// Enemy hits `target`: the game already split the hit into blocked / hp.
    protected void EnemyHits(Actor target, int blocked, int hp, bool killed = false) =>
        Engine.Handle(new DamageDealt(Enemy, target, blocked, hp, 0, killed, Source.Unknown));

    protected void Hit(Actor dealer, Actor target, int blocked, int hp, int overkill = 0, bool killed = false, string card = "STRIKE") =>
        Engine.Handle(new DamageDealt(dealer, target, blocked, hp, overkill, killed, Source.Card(card)));

    protected void Power(Actor applier, Actor target, string power, double amount, bool debuff = true) =>
        Engine.Handle(new PowerChanged(applier, target, power, amount, debuff));
}
