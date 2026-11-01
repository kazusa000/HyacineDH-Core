using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.DrinkMaker;

public class PacketGetDrinkMakerDataScRsp : BasePacket
{
    // The bar is not tracked yet. The empty fallback reply left the current drink unset and the level at 0: the client
    // threw in DrinkMakerBar._UpdateGameplayData, and 1034104's mission bartending (TriggerDrinkMakerBartendInMainMission)
    // stopped after the ice step, as the level-1 ingredients (DrinkMakerIngredientData) stayed locked.
    public PacketGetDrinkMakerDataScRsp() : base(CmdIds.GetDrinkMakerDataScRsp)
    {
        var proto = new GetDrinkMakerDataScRsp
        {
            Level = 1,
            AIBAPFGBJKA = new GODHAPDEGAN()
        };

        SetData(proto);
    }
}
