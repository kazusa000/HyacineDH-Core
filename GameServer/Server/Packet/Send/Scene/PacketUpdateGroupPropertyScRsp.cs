using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.Scene;

public class PacketUpdateGroupPropertyScRsp : BasePacket
{
    public PacketUpdateGroupPropertyScRsp(Retcode code) : base(CmdIds.UpdateGroupPropertyScRsp)
    {
        var proto = new UpdateGroupPropertyScRsp
        {
            Retcode = (uint)code
        };

        SetData(proto);
    }

    public PacketUpdateGroupPropertyScRsp(GroupPropertyRefreshData data, UpdateGroupPropertyCsReq req) : base(
        CmdIds.UpdateGroupPropertyScRsp)
    {
        var proto = new UpdateGroupPropertyScRsp
        {
            DimensionId = req.DimensionId,
            FloorId = req.FloorId,
            GroupId = (uint)data.GroupId,
            JDJOCEHPOKF = data.OldValue,
            EAGKBEMCFAB = data.NewValue,
            NOCBONMOOGC = data.PropertyName
        };

        SetData(proto);
    }
}
