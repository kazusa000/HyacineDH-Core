using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.DrinkMaker;

public class PacketMakeMissionDrinkScRsp : BasePacket
{
    // A mission drink is taken as made: the mission's own graph checks it against the formula before asking.
    public PacketMakeMissionDrinkScRsp(MakeMissionDrinkCsReq req) : base(CmdIds.MakeMissionDrinkScRsp)
    {
        var proto = new MakeMissionDrinkScRsp
        {
            GKALJIFKGNH = true,
            IsSave = req.IsSave,
            AIBAPFGBJKA = req.CHFKLHEGMGE ?? new GODHAPDEGAN()
        };

        SetData(proto);
    }
}
