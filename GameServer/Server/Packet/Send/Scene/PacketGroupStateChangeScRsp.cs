using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.Scene;

public class PacketGroupStateChangeScRsp : BasePacket
{
    public PacketGroupStateChangeScRsp(Retcode retcode, GroupStateInfo? info) : base(CmdIds.GroupStateChangeScRsp)
    {
        var proto = new GroupStateChangeScRsp { Retcode = (uint)retcode };
        if (info != null) proto.GroupStateInfo = info;

        SetData(proto);
    }
}
