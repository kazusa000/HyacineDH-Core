using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.MatchThree;

public class PacketMatchThreeGetDataScRsp : BasePacket
{
    public PacketMatchThreeGetDataScRsp() : base(CmdIds.MatchThreeGetDataDataScRsp)
    {
        SetData(new MatchThreeGetDataDataScRsp());
    }
}
