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

    // La vida va en una posición fija después de la de la entidad, no al final: en las capturas de la dungeon y del mini
    // boss casi todos los cuerpos traen más datos después, y leer los dos últimos varints daba basura (acá, 20 de 91).
    [Fact]
    public void Npc_spawn_health_is_read_after_the_position_not_from_the_end()
    {
        var events = CreateDecoder().Push(Frame(0x3641, Hex(RealCapture.NpcSpawnWithTailBody)), T0);

        var spawn = Assert.IsType<EntitySpawnEvent>(Assert.Single(events));
        Assert.Equal((34157UL, (uint?)2300215), (spawn.EntityId, spawn.NpcId));
        Assert.Equal(((long?)103148, (long?)435000), (spawn.Hp, spawn.MaxHp));
    }

    // Con el bit 0x08 en el u8 que sigue al npcId vienen 12 bytes más antes de la vida, sea cual sea el tipo. En la posición
    // de siempre los dos varints pasaban la validación (HP ≤ HPmax) con basura.
    [Theory]
    [InlineData(RealCapture.NpcSpawnWithExtraBlockBody, 28030UL, 2910001U, 4655L)]
    [InlineData(RealCapture.NpcSpawnWithExtraBlockType0DBody, 21692UL, 2600091U, 20_000_000L)]
    public void Npc_spawn_with_the_extra_block_has_its_health_twelve_bytes_later(string body, ulong entity, uint npc, long health)
    {
        var events = CreateDecoder().Push(Frame(0x3641, Hex(body)), T0);

        var spawn = Assert.IsType<EntitySpawnEvent>(Assert.Single(events));
        Assert.Equal((entity, (uint?)npc), (spawn.EntityId, spawn.NpcId));
        Assert.Equal(((long?)health, (long?)health), (spawn.Hp, spawn.MaxHp));
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
        // Armados con el layout de 0x8D00: entidad 20402, entrada u64 única de vida (02 01 00) o de otro valor (02 01 07,
        // como RealCapture.OtherResourceBody). Errors en 0: se ignora porque no trae la vida, no por un error de lectura.
        var hp = CreateDecoder().Push(Frame(0x8D00, Hex("b2 9f 01 02 01 00 a0 86 01 00 00 00 00 00")), T0);
        var otherDecoder = CreateDecoder();
        var other = otherDecoder.Push(Frame(0x8D00, Hex("b2 9f 01 02 01 07 a0 86 01 00 00 00 00 00")), T0);

        var health = Assert.IsType<HealthEvent>(Assert.Single(hp));
        Assert.Equal((20402UL, 100000L), (health.EntityId, health.Hp));
        Assert.Empty(other);
        Assert.Equal(0, otherDecoder.Stats.Errors);
    }

    [Fact]
    public void Decodes_real_boss_health_update()
    {
        var events = CreateDecoder().Push(Frame(0x8D00, Hex(RealCapture.BossHealthBody)), T0);

        // La mitad alta del u64 viene en 0, como en todas las lecturas de las capturas.
        var health = Assert.IsType<HealthEvent>(Assert.Single(events));
        Assert.Equal((22430UL, 9_857_918L), (health.EntityId, health.Hp));
    }

    [Fact]
    public void Real_update_of_another_resource_produces_no_event()
    {
        var decoder = CreateDecoder();

        Assert.Empty(decoder.Push(Frame(0x8D00, Hex(RealCapture.OtherResourceBody)), T0));
        Assert.Equal(0, decoder.Stats.Errors);
    }

    [Fact]
    public void Decodes_health_from_a_two_entry_update()
    {
        // La vida (id 00) y el HP máximo (id 07) en el mismo bloque u64: sale una sola lectura, la del id 00. En el cuerpo
        // real valen lo mismo, así que en la variante el id 07 pasa a 15.795 (el máximo de su 0x921B posterior).
        var decoder = CreateDecoder();
        var otherMax = Hex(RealCapture.TwoEntryHealthBody);
        otherMax[14] = 0xb3;
        otherMax[15] = 0x3d;

        var health = Assert.IsType<HealthEvent>(Assert.Single(decoder.Push(Frame(0x8D00, Hex(RealCapture.TwoEntryHealthBody)), T0)));
        var withOtherMax = Assert.IsType<HealthEvent>(Assert.Single(decoder.Push(Frame(0x8D00, otherMax), T0)));

        Assert.Equal((14895UL, 14_507L), (health.EntityId, health.Hp));
        Assert.Equal(14_507L, withOtherMax.Hp);
        Assert.Equal(0, decoder.Stats.Errors);
    }

    [Fact]
    public void Decodes_health_after_the_u32_block_of_a_flags_03_update()
    {
        var decoder = CreateDecoder();

        var health = Assert.IsType<HealthEvent>(Assert.Single(decoder.Push(Frame(0x8D00, Hex(RealCapture.FlagsBothHealthBody)), T0)));

        Assert.Equal((1244UL, 23_656L), (health.EntityId, health.Hp));
        Assert.Equal(0, decoder.Stats.Errors);
    }

    [Fact]
    public void Health_is_a_u64_bounded_to_the_u32_range()
    {
        // Armados: la vida es el u64 entero, no su mitad baja, con tope en uint.MaxValue (en las capturas la mitad alta
        // siempre vale 0 y la mayor vida es 89.988.206). Una mayor cuenta como error y no se emite: 2^32 con la mitad baja
        // salía 0, y 2^57 o una que no entra en un long desbordarían la cuenta de la verificación del motor.
        var decoder = CreateDecoder();

        var maxU32 = Assert.IsType<HealthEvent>(Assert.Single(decoder.Push(Frame(0x8D00, Hex("9e af 01 02 01 00 ff ff ff ff 00 00 00 00")), T0)));
        Assert.Equal(4_294_967_295L, maxU32.Hp);
        Assert.Equal(0, decoder.Stats.Errors);

        Assert.Empty(decoder.Push(Frame(0x8D00, Hex("9e af 01 02 01 00 00 00 00 00 01 00 00 00")), T0)); // 2^32
        Assert.Empty(decoder.Push(Frame(0x8D00, Hex("9e af 01 02 01 00 00 00 00 00 00 00 00 02")), T0)); // 2^57
        Assert.Empty(decoder.Push(Frame(0x8D00, Hex("9e af 01 02 01 00 ff ff ff ff ff ff ff ff")), T0)); // no entra en un long
        Assert.Equal(3, decoder.Stats.Errors);
    }

    // Armados a partir de RealCapture.BossHealthBody y TwoEntryHealthBody: ninguna forma de las capturas. Con bits de flags
    // desconocidos es otra forma y se ignora sin error, como un type desconocido de 0x3805. Si la gramática no cierra con el
    // cuerpo (termina antes de una entrada, sobran bytes o un n no coincide con las entradas) es un error de lectura: los
    // 0x8D00 de las capturas cierran exacto, así que una cola es un cambio de formato o una mala lectura y queda a la vista
    // en Errors. En los dos casos no sale ninguna lectura, tampoco la del id 00 que alcanzó a leerse.
    [Theory]
    [InlineData("9e af 01 06 01 00 7e 6b 96 00 00 00 00 00", 0)] // flags 0x06: bit 0x04 desconocido
    [InlineData("9e af 01 82 01 00 7e 6b 96 00 00 00 00 00", 0)] // flags 0x82
    [InlineData("9e af 01 02 01 00 7e 6b 96 00 00 00 00 00 00", 1)] // un byte de más
    [InlineData("af 74 02 01 00 ab 38 00 00 00 00 00 00 07 ab 38 00 00 00 00 00 00", 1)] // n = 1 con dos entradas: sobra la segunda
    [InlineData("9e af 01 02 01 00 7e 6b 96 00", 1)] // cortado en la mitad baja del u64
    [InlineData("af 74 02 02 00 ab 38 00 00 00 00 00 00 07 ab 38", 1)] // cortado en la segunda entrada, después de la vida
    [InlineData("9e af 01 02 ff 00 7e 6b 96 00 00 00 00 00", 1)] // n = 255 con una sola entrada
    [InlineData("9e af 01 03 03 01 94 03 00 00 01 00 7e 6b 96 00 00 00 00 00", 1)] // n del bloque u32 que se come la vida
    public void Malformed_health_update_produces_no_event(string body, int errors)
    {
        var decoder = CreateDecoder();

        Assert.Empty(decoder.Push(Frame(0x8D00, Hex(body)), T0));
        Assert.Equal(errors, decoder.Stats.Errors);
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
    public void Decodes_heal_start_tick_as_heal()
    {
        var events = CreateDecoder().Push(Frame(0x3805, Hex(RealCapture.HealStartTickBody)), T0);

        // 8411 es lo que lee hoy el decoder, no un monto validado: en la captura parece el total del HoT (T022).
        var heal = Assert.IsType<HealEvent>(Assert.Single(events));
        Assert.Equal((3000UL, 2000UL, 0x0104C6B6U, 8411L), (heal.SourceId, heal.TargetId, heal.SkillId, heal.Amount));
    }

    [Fact]
    public void Decodes_heal_tick_as_heal()
    {
        var events = CreateDecoder().Push(Frame(0x3805, Hex(RealCapture.HealTickBody)), T0);

        // Fija la lectura actual, no un monto validado: el 7851 baja 560 por tick, como si fuera el resto del HoT.
        // Sin la skill: en el 0x0B viene después de un varint que el decoder no lee (falta validar, T022).
        var heal = Assert.IsType<HealEvent>(Assert.Single(events));
        Assert.Equal((3000UL, 2000UL, 7851L), (heal.SourceId, heal.TargetId, heal.Amount));
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
    public void Absurd_length_counts_a_desync()
    {
        // Un largo declarado mayor que MaxFrameSize (1 MiB): el framer pierde sincronía y descarta lo pendiente.
        var decoder = CreateDecoder();

        Assert.Empty(decoder.Push(VarInts(2_000_000), T0));
        Assert.Equal(1, decoder.Stats.Desyncs);
    }

    [Fact]
    public void Absurd_length_inside_a_bundle_counts_a_desync()
    {
        // El framer de cada bundle es nuevo y se descarta: su desincronización tiene que sumarse igual. Errors en 0
        // asegura que no la tapó la excepción de un LZ4 inválido.
        var decoder = CreateDecoder();

        Assert.Empty(decoder.Push(Frame(0xFFFF, BundleDecoder.Compress(VarInts(2_000_000))), T0));
        Assert.Equal(1, decoder.Stats.Desyncs);
        Assert.Equal(0, decoder.Stats.Errors);
    }

    [Fact]
    public void Desyncs_add_up_across_pushes_and_streams_sharing_the_stats()
    {
        // El pipeline comparte un DecoderStats entre todos los flujos: cada Push suma solo lo suyo, sin pisar ni repetir.
        var opcodes = OpcodeTable.Load(Path.Combine(AppContext.BaseDirectory, "data", "opcodes.json"));
        var stats = new DecoderStats();
        var first = new GameStreamDecoder(opcodes, PacketDecoders.All, stats: stats);
        var second = new GameStreamDecoder(opcodes, PacketDecoders.All, stats: stats);

        first.Push(VarInts(2_000_000), T0);
        first.Push(VarInts(2_000_000), T0);
        second.Push(VarInts(2_000_000), T0);

        Assert.Equal(3, stats.Desyncs);
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
