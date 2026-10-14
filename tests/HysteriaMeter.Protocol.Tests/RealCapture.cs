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

    /// <summary>Muerte de la entidad 20402 a manos de 12157 ("Tester", legión "Legion").</summary>
    public const string KillBody = "b2 9f 01 54 c0 f4 00 fd 5e 79 05 06 54 65 73 74 65 72 06 4c 65 67 69 6f 6e 01 00 00 00 00 00 00 01 00";

    /// <summary>Tick de DoT (0x3805, type 0x0A) de la dungeon: objetivo 22430, fuente 1244, valor 1401, skill 0x00B87B76.</summary>
    public const string DotTickBody = "9e af 01 0a dc 09 07 1b e8 0f 48 f9 0a 76 7b b8 00";

    /// <summary>0x3805 con type 0x00: otro formato, sin monto.</summary>
    public const string PeriodicWithoutAmountBody = "a3 81 02 00 a3 81 02 01 0a e1 f5 05";

    /// <summary>
    /// Aparición de otro jugador (0x3645): entidad 16028, nombre reemplazado por "Tests".
    /// Recortado después del nombre; el real sigue ~1,5 KB.
    /// </summary>
    public const string PlayerSpawnBody = "9c 7d 0f b0 a4 01 07 05 54 65 73 74 73 1a 00 00 00 01 02";

    /// <summary>Invocación (0x3641, tipo 0x5F) con nombre de dueño reemplazado por "Tester": entidad 22627, NPC 2920650.</summary>
    public const string SummonWithOwnerBody = "e3 b0 01 5f 00 01 06 54 65 73 74 65 72 ca 90 2c 00 40 02";

    /// <summary>Invocación sin nombre de dueño: entidad 30599, NPC 2920655.</summary>
    public const string SummonWithoutOwnerBody = "87 ef 01 5f 00 00 cf 90 2c 00 40 02";

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
