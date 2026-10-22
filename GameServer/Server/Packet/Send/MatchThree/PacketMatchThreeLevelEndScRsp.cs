using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.MatchThree;

public class PacketMatchThreeLevelEndScRsp : BasePacket
{
    public PacketMatchThreeLevelEndScRsp(MatchThreeLevelEndCsReq req) : base(CmdIds.MatchThreeLevelEndScRsp)
    {
        SetData(new MatchThreeLevelEndScRsp { LevelId = req.LevelId, DGLAEPLNAFC = req.DGLAEPLNAFC });
    }
}
