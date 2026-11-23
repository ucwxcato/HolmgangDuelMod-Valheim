using Catosaurluna.HolmgangDuelMod.Core.Domain;

namespace Catosaurluna.HolmgangDuelMod.Tests;

public sealed class DuelPvpRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Players_outside_any_duel_keep_vanilla_pvp()
    {
        var manager = new DuelManager();

        Assert.Equal(DamageDecision.Allow, DuelDamageRules.Evaluate(manager, "x", "y"));
    }

    [Fact]
    public void Active_pair_may_damage_each_other()
    {
        var manager = new DuelManager();
        StartDuel(manager, "a", "b", active: true, out _);

        Assert.Equal(DamageDecision.Allow, DuelDamageRules.Evaluate(manager, "a", "b"));
        Assert.Equal(DamageDecision.Allow, DuelDamageRules.Evaluate(manager, "b", "a"));
    }

    [Fact]
    public void Pair_is_blocked_during_countdown()
    {
        var manager = new DuelManager();
        StartDuel(manager, "a", "b", active: false, out _);

        Assert.Equal(DamageDecision.Block, DuelDamageRules.Evaluate(manager, "a", "b"));
    }

    [Fact]
    public void Outsider_cannot_damage_a_duelist()
    {
        var manager = new DuelManager();
        StartDuel(manager, "a", "b", active: true, out _);

        Assert.Equal(DamageDecision.Block, DuelDamageRules.Evaluate(manager, "c", "a"));
    }

    [Fact]
    public void Duelist_cannot_damage_an_outsider()
    {
        var manager = new DuelManager();
        StartDuel(manager, "a", "b", active: true, out _);

        Assert.Equal(DamageDecision.Block, DuelDamageRules.Evaluate(manager, "a", "c"));
    }

    [Fact]
    public void Duelists_of_different_duels_cannot_damage_each_other()
    {
        var manager = new DuelManager();
        StartDuel(manager, "a", "b", active: true, out _);
        StartDuel(manager, "c", "d", active: true, out _);

        Assert.Equal(DamageDecision.Block, DuelDamageRules.Evaluate(manager, "a", "c"));
    }

    [Fact]
    public void Self_damage_inside_a_duel_is_blocked()
    {
        var manager = new DuelManager();
        StartDuel(manager, "a", "b", active: true, out _);

        Assert.Equal(DamageDecision.Block, DuelDamageRules.Evaluate(manager, "a", "a"));
    }

    [Fact]
    public void Non_player_attackers_and_targets_are_not_filtered()
    {
        var manager = new DuelManager();
        StartDuel(manager, "a", "b", active: true, out _);

        Assert.Equal(DamageDecision.Allow, DuelDamageRules.Evaluate(manager, null, "a"));
        Assert.Equal(DamageDecision.Allow, DuelDamageRules.Evaluate(manager, "a", null));
    }

    [Fact]
    public void Result_text_names_the_winner_and_reason()
    {
        var manager = new DuelManager();
        var session = StartDuel(manager, "a", "b", active: true, out _);

        var text = DuelResultText.Describe(session, DuelResult.Winner(DuelEndReason.HealthThreshold, "b", "a"));

        Assert.Equal("Bob won the duel against Alice: defeated.", text);
    }

    [Fact]
    public void Result_text_describes_a_draw()
    {
        var manager = new DuelManager();
        var session = StartDuel(manager, "a", "b", active: true, out _);

        var text = DuelResultText.Describe(session, DuelResult.Draw(DuelEndReason.Cancelled));

        Assert.Equal("Duel between Alice and Bob ended in a draw: cancelled.", text);
    }

    private static DuelSession StartDuel(DuelManager manager, string firstId, string secondId, bool active, out DuelSession session)
    {
        var first = new DuelParticipant(firstId, firstId == "a" ? "Alice" : "Bob");
        var second = new DuelParticipant(secondId, secondId == "b" ? "Bob" : secondId.ToUpperInvariant());
        Assert.True(manager.TryCreateRequest(first, second, Start, TimeSpan.FromSeconds(30), out _));
        Assert.True(manager.TryAcceptRequest(second.StableId, first.StableId, new DuelPosition(0, 0, 0), 15, Start, TimeSpan.FromSeconds(5), out var created));
        session = created!;
        if (active)
            session.StartActive(Start.AddSeconds(5));
        return session;
    }
}
