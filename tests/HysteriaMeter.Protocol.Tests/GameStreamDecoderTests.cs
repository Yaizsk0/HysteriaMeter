using HysteriaMeter.Protocol.Decoding;
using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;
using static HysteriaMeter.Protocol.Tests.FrameBuilder;

namespace HysteriaMeter.Protocol.Tests;

public class GameStreamDecoderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 21, 0, 0, TimeSpan.Zero);

    private static GameStreamDecoder CreateDecoder() =>
        new(OpcodeTable.Load(Path.Combine(AppContext.BaseDirectory, "data", "opcodes.json")), PacketDecoders.All);

    [Fact]
    public void Decodes_real_damage_frame()
    {
        var events = CreateDecoder().Push(Frame(0x3804, Hex(RealCapture.DamageBody)), T0);

        var damage = Assert.IsType<DamageEvent>(Assert.Single(events));
        Assert.Equal((12157UL, 43190UL, 0x00FF4791U, 672L), (damage.SourceId, damage.TargetId, damage.SkillId, damage.Amount));
        Assert.Equal(T0, damage.Timestamp);
    }

    [Fact]
    public void Decodes_damage_frame_with_extra_block()
    {
        var events = CreateDecoder().Push(Frame(0x3804, Hex(RealCapture.DamageWithExtraBody)), T0);

        var damage = Assert.IsType<DamageEvent>(Assert.Single(events));
        Assert.Equal(2221, damage.Amount);
        Assert.Equal(HitModifiers.MultiHit | HitModifiers.FrontAttack, damage.Modifiers);
    }

    [Theory]
    [InlineData(12, 0x03, HitModifiers.Critical | HitModifiers.MultiHit | HitModifiers.FrontAttack)]
    [InlineData(15, 0x01, HitModifiers.MultiHit | HitModifiers.BackAttack)]
    [InlineData(13, 0x88, HitModifiers.MultiHit | HitModifiers.FrontAttack | HitModifiers.DoubleHit)]
    [InlineData(13, 0x84, HitModifiers.MultiHit | HitModifiers.FrontAttack | HitModifiers.Perfect)]
    public void Decodes_hit_modifiers(int offset, byte value, HitModifiers expected)
    {
        // Variantes del frame real: offset 12 = type, 13 = hitFlags, 15 = lado.
        var body = Hex(RealCapture.DamageWithExtraBody);
        body[offset] = value;

        var damage = Assert.IsType<DamageEvent>(Assert.Single(CreateDecoder().Push(Frame(0x3804, body), T0)));
        Assert.Equal(expected, damage.Modifiers);
        Assert.Equal(2221, damage.Amount);
    }

    [Fact]
    public void Hit_without_extra_block_has_no_side()
    {
        var body = Hex(RealCapture.DamageBody);
        body[3] = 0x04; // sin bloque extra

        var damage = Assert.IsType<DamageEvent>(Assert.Single(CreateDecoder().Push(Frame(0x3804, Concat(body[..13], body[16..])), T0)));
        Assert.Equal(HitModifiers.None, damage.Modifiers);
        Assert.Equal(672, damage.Amount);
    }

    [Fact]
    public void Effect_without_damage_flag_produces_no_event()
    {
        Assert.Empty(CreateDecoder().Push(Frame(0x3804, Hex(RealCapture.EffectWithoutDamageBody)), T0));
    }

    [Fact]
    public void Decodes_npc_spawn()
    {
        var events = CreateDecoder().Push(Frame(0x3641, Hex(RealCapture.NpcSpawnBody)), T0);

        var spawn = Assert.IsType<EntitySpawnEvent>(Assert.Single(events));
        Assert.Equal((20402UL, (uint?)0x00203430), (spawn.EntityId, spawn.NpcId));
        Assert.Equal(((long?)119700, (long?)119700), (spawn.Hp, spawn.MaxHp));
    }

    [Fact]
    public void Spawn_without_health_fields_still_decodes()
    {
        var events = CreateDecoder().Push(Frame(0x3641, Hex(RealCapture.SummonWithOwnerBody)), T0);

        var spawn = Assert.IsType<EntitySpawnEvent>(Assert.Single(events));
        Assert.Equal(((long?)null, (long?)null), (spawn.Hp, spawn.MaxHp));
    }

    [Fact]
    public void Decodes_health_update_and_ignores_other_resources()
    {
        // Armados con el layout de 0x8D00: entidad 20402, prefijo 02 01 00 (vida) o 02 02 00 (otro recurso).
        var hp = CreateDecoder().Push(Frame(0x8D00, Hex("b2 9f 01 02 01 00 a0 86 01 00")), T0);
        var other = CreateDecoder().Push(Frame(0x8D00, Hex("b2 9f 01 02 02 00 a0 86 01 00")), T0);

        var health = Assert.IsType<HealthEvent>(Assert.Single(hp));
        Assert.Equal((20402UL, 100000L), (health.EntityId, health.Hp));
        Assert.Empty(other);
    }

    [Fact]
    public void Decodes_summon_spawn_with_owner_name()
    {
        var events = CreateDecoder().Push(Frame(0x3641, Hex(RealCapture.SummonWithOwnerBody)), T0);

        var spawn = Assert.IsType<EntitySpawnEvent>(Assert.Single(events));
        Assert.Equal((22627UL, (uint?)2920650, "Tester"), (spawn.EntityId, spawn.NpcId, spawn.OwnerName));
    }

    [Fact]
    public void Decodes_summon_spawn_without_owner_name()
    {
        var events = CreateDecoder().Push(Frame(0x3641, Hex(RealCapture.SummonWithoutOwnerBody)), T0);

        var spawn = Assert.IsType<EntitySpawnEvent>(Assert.Single(events));
        Assert.Equal((30599UL, (uint?)2920655, (string?)null), (spawn.EntityId, spawn.NpcId, spawn.OwnerName));
    }

    [Fact]
    public void Decodes_dot_tick_as_damage_over_time()
    {
        var events = CreateDecoder().Push(Frame(0x3805, Hex(RealCapture.DotTickBody)), T0);

        var damage = Assert.IsType<DamageEvent>(Assert.Single(events));
        Assert.Equal((1244UL, 22430UL, 0x00B87B76U, 1401L), (damage.SourceId, damage.TargetId, damage.SkillId, damage.Amount));
        Assert.Equal(HitModifiers.DamageOverTime, damage.Modifiers);
    }

    [Fact]
    public void Decodes_heal_tick_as_heal()
    {
        var body = Hex(RealCapture.DotTickBody);
        body[3] = 0x0B; // mismo layout, tipo curación

        var heal = Assert.IsType<HealEvent>(Assert.Single(CreateDecoder().Push(Frame(0x3805, body), T0)));
        Assert.Equal((1244UL, 22430UL, 1401L), (heal.SourceId, heal.TargetId, heal.Amount));
    }

    [Fact]
    public void Periodic_tick_without_amount_produces_no_event()
    {
        var decoder = CreateDecoder();

        Assert.Empty(decoder.Push(Frame(0x3805, Hex(RealCapture.PeriodicWithoutAmountBody)), T0));
        Assert.Equal(0, decoder.Stats.Errors);
    }

    [Fact]
    public void Decodes_player_spawn_name()
    {
        var events = CreateDecoder().Push(Frame(0x3645, Hex(RealCapture.PlayerSpawnBody)), T0);

        var info = Assert.IsType<EntityInfoEvent>(Assert.Single(events));
        Assert.Equal((16028UL, "Tests"), (info.EntityId, info.Name));
    }

    [Fact]
    public void Player_spawn_with_garbage_name_produces_no_event()
    {
        var body = Hex(RealCapture.PlayerSpawnBody);
        body[8] = 0x01; // byte de control dentro del "nombre": otro layout

        Assert.Empty(CreateDecoder().Push(Frame(0x3645, body), T0));
    }

    [Fact]
    public void Decodes_local_player_entity()
    {
        var events = CreateDecoder().Push(Frame(0x364A, Hex(RealCapture.LocalPlayerBody)), T0);

        var local = Assert.IsType<LocalPlayerEvent>(Assert.Single(events));
        Assert.Equal((1879UL, (string?)null), (local.EntityId, local.Name));
    }

    [Fact]
    public void Decodes_local_player_name()
    {
        var events = CreateDecoder().Push(Frame(0x3633, Hex(RealCapture.LocalPlayerInfoBody)), T0);

        var local = Assert.IsType<LocalPlayerEvent>(Assert.Single(events));
        Assert.Equal((1698UL, "Tests"), (local.EntityId, local.Name));
    }

    [Fact]
    public void Decodes_item_level_and_power_of_other_players()
    {
        var body = Hex(RealCapture.PlayerSpawnBody).Concat(Hex(RealCapture.PlayerGearSections)).ToArray();

        var events = CreateDecoder().Push(Frame(0x3645, body), T0);

        Assert.IsType<EntityInfoEvent>(events[0]);
        var gear = Assert.IsType<PlayerGearEvent>(events[1]);
        Assert.Equal((16028UL, (int?)2001, (long?)93_571), (gear.EntityId, gear.ItemLevel, gear.CombatPower));
        Assert.Equal(161_964, PlayerGear.FindCombatPower(Hex(RealCapture.PowerWithExtraByteTail)));
        Assert.Null(PlayerGear.FindItemLevel(Hex(RealCapture.PowerWithExtraByteTail)));
    }

    [Fact]
    public void Decodes_local_item_level_and_power()
    {
        var decoder = CreateDecoder();

        var info = decoder.Push(Frame(0x3633, Hex(RealCapture.LocalPlayerInfoWithItemLevelBody)), T0);
        var power = decoder.Push(Frame(0x3656, Hex(RealCapture.LocalCombatPowerBody)), T0);

        Assert.Equal(2, info.Count);
        Assert.Equal((1698UL, (int?)2046), (Assert.IsType<PlayerGearEvent>(info[1]).EntityId, ((PlayerGearEvent)info[1]).ItemLevel));
        Assert.Equal(91_358, Assert.IsType<LocalCombatPowerEvent>(Assert.Single(power)).CombatPower);
    }

    [Fact]
    public void Decodes_player_spawn_when_field_after_entity_starts_below_0x80()
    {
        var events = CreateDecoder().Push(Frame(0x3645, Hex(RealCapture.PlayerSpawnLowByteBody)), T0);

        var info = Assert.IsType<EntityInfoEvent>(Assert.Single(events));
        Assert.Equal((14895UL, "Tests"), (info.EntityId, info.Name));
    }

    [Fact]
    public void Decodes_party_member_entity()
    {
        var events = CreateDecoder().Push(Frame(0x921B, Hex(RealCapture.PartyMemberBody)), T0);

        Assert.Equal(15000UL, Assert.IsType<PartyMemberSeenEvent>(Assert.Single(events)).EntityId);
    }

    [Fact]
    public void Decodes_kill_with_killer_name()
    {
        var events = CreateDecoder().Push(Frame(0x8D04, Hex(RealCapture.KillBody)), T0);

        Assert.Collection(
            events,
            e => Assert.Equal(20402UL, Assert.IsType<DeathEvent>(e).EntityId),
            e =>
            {
                var info = Assert.IsType<EntityInfoEvent>(e);
                Assert.Equal((12157UL, "Tester"), (info.EntityId, info.Name));
            });
    }

    [Fact]
    public void Expands_real_lz4_bundle()
    {
        var decoder = CreateDecoder();
        decoder.Push(Hex(RealCapture.BundleFrame), T0);

        Assert.Equal(0, decoder.Stats.Errors);
        Assert.Equal(1, decoder.Stats.ByOpcode[0xFFFF]);
        Assert.True(decoder.Stats.ByOpcode.Count > 1);
    }

    [Fact]
    public void Expands_synthetic_bundle_with_damage()
    {
        var inner = Concat(Frame(0x3804, Hex(RealCapture.DamageBody)), Frame(0x3804, Hex(RealCapture.DamageWithExtraBody)));
        var events = CreateDecoder().Push(Frame(0xFFFF, BundleDecoder.Compress(inner)), T0);

        Assert.Equal([672L, 2221L], events.Cast<DamageEvent>().Select(e => e.Amount));
    }

    [Fact]
    public void Truncated_packet_is_counted_not_thrown()
    {
        var decoder = CreateDecoder();
        var events = decoder.Push(Frame(0x3804, Hex(RealCapture.DamageBody)[..10]), T0);

        Assert.Empty(events);
        Assert.Equal(1, decoder.Stats.Errors);
    }

    [Fact]
    public void Unknown_opcodes_are_counted()
    {
        var decoder = CreateDecoder();
        decoder.Push(Concat(Frame(0x7777, 1), Frame(0x7777, 2)), T0);

        Assert.Equal(2, decoder.Stats.UnknownFrames);
        Assert.Equal(2, decoder.Stats.ByOpcode[0x7777]);
    }
}
