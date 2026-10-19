namespace HysteriaMeter.Protocol.Tests;

/// <summary>
/// Bytes copiados de una captura real del cliente global (2026-10-14), dirección servidor → cliente.
/// Los nombres de personaje y legión fueron reemplazados.
/// </summary>
internal static class RealCapture
{
    /// <summary>Inicio del stream: dos movimientos (0x371D) y un 0x3600.</summary>
    public const string ThreeFrames = "11 1d 37 84 d9 02 26 fc 0a 47 51 47 51 01 11 1d 37 e4 e8 02 26 f7 fa 48 98 48 98 01 0e 00 36 b7 3a 4d 3c a1 01 00 00";

    /// <summary>Daño (flags 0x06): objetivo 43190, fuente 12157, skill 0x00FF4791, valor 672.</summary>
    public const string DamageBody = "b6 d1 02 06 00 fd 5e 91 47 ff 00 7a 02 00 00 02 af f4 b7 63 01 00 00 00 86 5d a0 05 01 00";

    /// <summary>Daño con bloque extra (flags 0x36): valor 2221.</summary>
    public const string DamageWithExtraBody = "b6 d1 02 36 00 fd 5e d0 47 f6 00 79 02 80 00 02 4b 0d 34 60 01 00 00 00 86 5d ad 11 03 33 33 33 01 00";

    /// <summary>Efecto sin daño (flags 0x00).</summary>
    public const string EffectWithoutDamageBody = "b6 d1 02 00 00 fd 5e fa 6d f6 00 74 02 b5 f5 42 60 01 00 00 00 86 5d 01 00";

    /// <summary>Aparición de NPC: entidad 20402, NPC 0x00203430.</summary>
    public const string NpcSpawnBody = "b2 9f 01 04 20 00 30 34 20 00 00 02 db 96 ae 47 ee f3 3e c8 00 00 82 42 f2 31 9f 42 9a 38 01 94 a7 07 94 a7 07";

    /// <summary>
    /// Aparición de un monstruo con datos después de la vida (dungeon): entidad 34157, NPC 2300215, vida 103.148 de
    /// 435.000 (<c>ec a5 06 b8 c6 1a</c>). Los dos últimos varints (20 y 91) no son la vida.
    /// </summary>
    public const string NpcSpawnWithTailBody =
        "ed 8a 02 0c 20 00 37 19 23 00 40 02 31 59 8d 47 2a 19 36 c7 00 9c 12 47 df 10 b3 42 2c 5e 01 ec a5 06 b8 c6 1a "
        + "64 00 00 00 64 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 dc 05 00 00 dc 05 00 00 01 00 00 00 "
        + "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 01 06 03 11 01 81 96 98 00 ff ff ff ff ff ff ff ff 80 75 d5 "
        + "2a bb 03 00 00 ed 8a 02 01 08 4e 30 81 47 87 f4 18 c7 00 98 0a 47 11 02 84 96 98 00 ff ff ff ff ff ff ff ff "
        + "80 75 d5 2a bb 03 00 00 ed 8a 02 01 4e 30 81 47 87 f4 18 c7 00 98 0a 47 13 04 cb 99 31 0b 88 13 00 00 00 00 "
        + "00 00 bf 10 63 3c a1 01 00 00 87 6a 0d 61 8f 1e 01 d1 e3 8d 47 1d b0 35 c7 00 9c 12 47 01 00 36 00 00 00 41 "
        + "6e 14 db 00";

    /// <summary>
    /// Aparición con el bit 0x08 en el u8 que sigue al npcId (mini boss): entidad 28030, NPC 2910001, tipo 0x85. La vida
    /// (4.655 de 4.655, <c>af 24 af 24</c>) viene 12 bytes más adelante; en la posición de siempre se leía 23 de 234.659.361.
    /// </summary>
    public const string NpcSpawnWithExtraBlockBody =
        "fe da 01 85 21 00 31 67 2c 00 08 02 c4 36 13 c8 79 14 0e 48 ec 49 2e 47 2a 8a 6e 43 9a a9 16 17 a1 bc f2 6f 03 bd b8 3b "
        + "46 bb 01 af 24 af 24 64 00 00 00 64 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 64 00 00 00 64 00 00 00 01 "
        + "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 0e 00 08 01 00 14 00 00 00 07 c9 c1 2d 2b 1f ec 1b e5 e8 20 00 "
        + "56 16 80 ac 00";

    /// <summary>
    /// Lo mismo con tipo 0x0D (login): entidad 21692, NPC 2600091, vida 20.000.000 de 20.000.000 (<c>80 da c4 09</c>), la misma
    /// que traen sus apariciones sin el bit. En la posición de siempre se leía 43 de 1.679.750.
    /// </summary>
    public const string NpcSpawnWithExtraBlockType0DBody =
        "bc a9 01 0d 20 00 9b ac 27 00 08 02 b0 3e e4 c6 1e ff c2 46 e0 44 c6 46 94 4f 0c 43 c7 63 bd 2b 86 c3 66 b2 5e 43 6e d8 "
        + "ef 41 01 80 da c4 09 80 da c4 09 64 00 00 00 64 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 50 c3 00 00 50 "
        + "c3 00 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 03 02 11 01 81 96 98 00 ff ff ff ff ff ff ff ff 80 "
        + "75 d5 2a bb 03 00 00 bc a9 01 01 04 7c a8 e6 c6 72 04 c7 46 e7 1a c6 46 11 02 84 96 98 00 ff ff ff ff ff ff ff ff 80 75 "
        + "d5 2a bb 03 00 00 bc a9 01 01 7c a8 e6 c6 72 04 c7 46 e7 1a c6 46 09 01 00 32 00 00 00 3d f0 90 31 00";

    /// <summary>Muerte de la entidad 20402 a manos de 12157 ("Tester", legión "Legion").</summary>
    public const string KillBody = "b2 9f 01 54 c0 f4 00 fd 5e 79 05 06 54 65 73 74 65 72 06 4c 65 67 69 6f 6e 01 00 00 00 00 00 00 01 00";

    /// <summary>Tick de DoT (0x3805, type 0x0A) de la dungeon: objetivo 22430, fuente 1244, valor 1401, skill 0x00B87B76.</summary>
    public const string DotTickBody = "9e af 01 0a dc 09 07 1b e8 0f 48 f9 0a 76 7b b8 00";

    /// <summary>
    /// Primera lectura de vida (0x8D00) del boss de la dungeon (21:48:31 UTC): entidad 22430 (npc 2300218, el objetivo de
    /// <see cref="DotTickBody"/>), 9.857.918 = 9.860.000 de su spawn menos el primer golpe (2.082). La vida es un u64: su
    /// mitad alta vale <c>00 00 00 00</c> en los 31.988 valores del bloque u64 de las seis capturas.
    /// </summary>
    public const string BossHealthBody = "9e af 01 02 01 00 7e 6b 96 00 00 00 00 00";

    /// <summary>
    /// 0x8D00 de otro recurso (prefijo <c>02 01 07</c>) de un compañero de party, entidad 14895: 15.795, el HP máximo que
    /// trae su 0x921B. Entrada única con id 07: no es la vida (id 00).
    /// </summary>
    public const string OtherResourceBody = "af 74 02 01 07 b3 3d 00 00 00 00 00 00";

    /// <summary>
    /// El único 0x8D00 <c>02 02 00</c> de la dungeon, de la misma entidad 14895, 400 ms antes de
    /// <see cref="OtherResourceBody"/>. No es otro recurso: el segundo byte cuenta entradas (id + 8 bytes), y trae la vida
    /// (id 00, 14.507) y el HP máximo (id 07, 14.507). Los 0x8D00 de las seis capturas cierran con
    /// <c>u8 flags | [u8 n | (id u32)×n] si flags &amp; 1 | [u8 n | (id u64)×n] si flags &amp; 2</c>, y en los siguientes de
    /// esta entidad el id 00 y el id 07 coinciden con su 0x921B (14.567/15.795).
    /// </summary>
    public const string TwoEntryHealthBody = "af 74 02 02 00 ab 38 00 00 00 00 00 00 07 ab 38 00 00 00 00 00 00";

    /// <summary>
    /// 0x8D00 con flags 03 del jugador local de la dungeon (21:46:08 UTC), entidad 1244 (la fuente de
    /// <see cref="DotTickBody"/>): un bloque u32 (id 01, 916, sin identificar) y la vida en el bloque u64 (id 00, 23.656).
    /// La vida cierra con la lectura <c>02 01 00</c> anterior de la entidad (24.945, 452 ms antes) menos el único golpe que
    /// recibió en el medio (1.289, un 0x3804 del npc 2920204); el local no tiene 0x921B para comparar. Es una de las 104
    /// lecturas de esa entidad con flags 03.
    /// </summary>
    public const string FlagsBothHealthBody = "dc 09 03 01 01 94 03 00 00 01 00 68 5c 00 00 00 00 00 00";

    /// <summary>0x3805 con type 0x00: otro formato, sin monto.</summary>
    public const string PeriodicWithoutAmountBody = "a3 81 02 00 a3 81 02 01 0a e1 f5 05";

    /// <summary>
    /// Tick de curación (0x3805, type 0x09) de la captura de comparación (2026-10-17): objetivo 2000, fuente 3000
    /// (IDs reemplazados, con el mismo largo de varint), skill 0x0104C6B6 y 8411, lo que lee hoy el decoder como
    /// monto. En la captura parece el total del HoT y no lo curado: 8411 = 7851 + 560 del primer 0x0B
    /// (<see cref="HealTickBody"/>). Fuente y objetivo distintos: el 0x09 no es solo la curación propia.
    /// </summary>
    public const string HealStartTickBody ="d0 0f 09 b8 17 f6 04 23 9f dd 65 db 41 b6 c6 04 01";

    /// <summary>
    /// Tick de curación (0x3805, type 0x0B) del mismo efecto que <see cref="HealStartTickBody"/> (misma instancia,
    /// fuente, objetivo y skill), 2 s después: 7851, lo que lee hoy el decoder. Después viene otro varint (560) y
    /// recién ahí la skill; así cierran los 3309 ticks 0x0B de la captura. En los 14 ticks del efecto el 7851 baja
    /// exactamente 560 por tick hasta 571: parece el resto del HoT, no lo curado (falta validar, T022).
    /// </summary>
    public const string HealTickBody = "d0 0f 0b b8 17 f6 04 23 9f dd 65 ab 3d b0 04 b6 c6 04 01";

    /// <summary>
    /// Aparición de otro jugador (0x3645): entidad 16028, nombre reemplazado por "Tests".
    /// Recortado después del nombre; el real sigue ~1,5 KB.
    /// </summary>
    public const string PlayerSpawnBody = "9c 7d 0f b0 a4 01 07 05 54 65 73 74 73 1a 00 00 00 01 02";

    /// <summary>Invocación (0x3641, tipo 0x5F) con nombre de dueño reemplazado por "Tester": entidad 22627, NPC 2920650.</summary>
    public const string SummonWithOwnerBody = "e3 b0 01 5f 00 01 06 54 65 73 74 65 72 ca 90 2c 00 40 02";

    /// <summary>Invocación sin nombre de dueño: entidad 30599, NPC 2920655.</summary>
    public const string SummonWithoutOwnerBody = "87 ef 01 5f 00 00 cf 90 2c 00 40 02";

    /// <summary>
    /// Invocación de un bloque sin nombre de dueño (0x3641 tipo 0x5F, dungeon, 21:45:16 UTC), completa: entidad 17893,
    /// NPC 2920655 (el espíritu del Hechicero, que pega con Bittercold Wind), vida 15.438 de 15.438 (<c>ce 78 ce 78</c>). En
    /// la cola (byte 35) viene una sola marca <c>80 75 d5 2a bb 03 00 00</c> (byte 69 de la cola) y después <c>e5 8b 01</c>
    /// (e2 = 17893, la propia invocación), <c>01</c> (k1), <c>02</c>, la posición del spawn y <c>07 02 06 9c 3e 00 00</c>: el
    /// dueño, 16028 (byte 97 de la cola), el jugador de clase 15 de la dungeon (el de <see cref="PlayerSpawnBody"/>), que
    /// es el único de su clase en la party. La cadena de 8 bytes con prefijo de largo que sigue (INTERPRETADO: el nombre de
    /// la legión) se reemplazó por "Legiones"; el resto son los bytes de la captura.
    /// </summary>
    public const string SummonWithOwnerInTailBody =
        "e5 8b 01 5f 00 00 cf 90 2c 00 40 02 6b 6e f7 44 a9 63 64 c5 00 00 1c c3 c3 a4 8a 43 2f c5 01 ce "
        + "78 ce 78 3e 0e 00 00 3e 0e 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 3f 02 00 64 00 00 00 f0 "
        + "49 02 00 01 00 00 00 00 00 00 00 a0 86 01 00 00 00 00 00 d0 4e 09 00 01 01 01 11 01 81 96 98 00 "
        + "ff ff ff ff ff ff ff ff 80 75 d5 2a bb 03 00 00 e5 8b 01 01 02 6b 6e f7 44 a9 63 64 c5 00 00 1c "
        + "c3 07 02 06 9c 3e 00 00 de 00 00 00 00 00 79 05 08 4c 65 67 69 6f 6e 65 73 01 00 01 00 00 00 00 "
        + "00 00 00 00 00 00 00 00 00 00 00 02 cd 00 5e 09 00 00 d0 00 73 01 00 00 2d 00 00 00 00";

    /// <summary>Paquete del jugador local (0x364A, captura del mini boss): entidad 1879.</summary>
    public const string LocalPlayerBody = "d7 0e 01 38 00 e0 00 00 00 00 00 00 00 00 00 00 00";

    /// <summary>
    /// Datos del jugador local al entrar a una zona (0x3633, captura del login): entidad 1698, nombre
    /// reemplazado por "Tests". Recortado; el real sigue ~2,3 KB.
    /// </summary>
    public const string LocalPlayerInfoBody = "a2 0d 5f 91 c3 2b 3f 05 54 65 73 74 73 79 05 09 00 00 00 01 2d 00 00 00";

    /// <summary>
    /// Igual que <see cref="LocalPlayerInfoBody"/> pero sin recortar el item level (2046) que sigue al nivel (45).
    /// </summary>
    public const string LocalPlayerInfoWithItemLevelBody = "a2 0d 5f 91 c3 2b 3f 05 54 65 73 74 73 79 05 09 00 00 00 01 2d 00 00 00 fe 07 00 00 fe 07 00 00 2d 00";

    /// <summary>Power del jugador local (0x3656, captura del login): 91.358 (u64) y 91.375.</summary>
    public const string LocalCombatPowerBody = "de 64 01 00 00 00 00 00 ef 64 01 00 00 00 00 00";

    /// <summary>
    /// Secciones de equipo de una aparición de otro jugador (0x3645, mini boss), con el resto del paquete
    /// recortado: personaje | 00 00 | servidor 0x0579 | 4 tags | nivel 45 | 0 | item level 2001, y al final
    /// el marcador del power con 93.571 (u64) y un 00.
    /// </summary>
    public const string PlayerGearSections =
        "03 0e 01 af 67 04 00 00 00 79 05 04 cd 00 c4 03 00 00 ce 00 60 f0 ff ff d0 00 73 01 00 00 27 02 48 f4 ff ff "
        + "2d 00 00 00 00 00 00 00 d1 07 00 00 de 02 0c 50 64 64 "
        + "00 00 00 01 24 f4 cb 1f e9 03 00 00 10 00 00 00 83 6d 01 00 00 00 00 00 00";

    /// <summary>Cola del power con un byte extra antes del u64 (login): 161.964.</summary>
    public const string PowerWithExtraByteTail = "03 24 f4 cb 1f 3f 05 00 00 0d 00 00 00 03 ac 78 02 00 00 00 00 00 00";

    /// <summary>HP/MP de un miembro de la party (0x921B, dungeon en party): entidad 15000, HP 16915/16915.</summary>
    public const string PartyMemberBody = "98 75 93 84 01 93 84 01 62 10 00 00 af 12 00 00 00 00 00 00 00 00 00 00 42 0e 00 00 f0 49 02 00 01";

    /// <summary>
    /// Aparición de otro jugador (0x3645, mini boss) cuyo u16 tras la entidad empieza con un byte &lt; 0x80:
    /// entidad 14895, nombre reemplazado por "Tests". Recortado.
    /// </summary>
    public const string PlayerSpawnLowByteBody = "af 74 0f 38 a0 01 07 05 54 65 73 74 73 1a 00 00 00";

    /// <summary>Frame bundle (0xFFFF) completo: 431 bytes con LZ4 adentro.</summary>
    public const string BundleFrame = """
        b103ffff20020000f30e970401910000f203000018018f9506003e07c7006d0ec70058b8457f000100f301018b950600feb7c7000f2ac700383c4617
        00f60100019495060051a44700452e480090c41800f5008a95060034d5c600c79dc7008087433000e689950600ad60c7805aa6c700981e3000e68c95
        0600768dc6004c23c6001ca81800e58d95060012d3c600be9b460052901800f50e008e950640e95d3ca101000001909506000762c78062a14700c84f
        46ff6d00e5919506647864c7d0175a470044213d00f50101929506004692468098b947001e05473000e69b9506c83779c749aee747003e8e3000e693
        95060736c047d24a914700442b1800e6959506800ef44700e02c4600f0e41800e6969506c0080c48002b0a4700b6ec1800e6979506000de547005ebc
        c70054281800f5019895061f81ff47cd0099c700d839460f9100e6999506f66a34c7f8e7c2470046823100e69a9506a3ecc8c60d0f014800ba9b1800
        e59c9506c07f1b4880d2ffc7009c00180071009d9506ba42982e0172009e9506e108b00c00709f9506e9598d3d46017200a09506a71d4b0c00d00000
        0e00366c3e4d3ca1010000
        """;
}
