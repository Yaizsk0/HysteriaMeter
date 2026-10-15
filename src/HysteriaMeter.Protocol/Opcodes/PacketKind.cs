namespace HysteriaMeter.Protocol.Opcodes;

/// <summary>Tipos de paquete que nos interesan. Los nombres coinciden con las claves de <c>opcodes.json</c>.</summary>
public enum PacketKind
{
    Unknown = 0,
    Bundle,
    LocalPlayer,
    LocalPlayerInfo,
    EntitySpawn,
    EntityInfo,
    Damage,
    PeriodicEffect,
    Heal,
    Death,
}
