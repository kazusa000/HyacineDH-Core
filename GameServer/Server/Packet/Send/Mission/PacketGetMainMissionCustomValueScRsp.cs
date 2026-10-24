using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.Mission;

public class PacketGetMainMissionCustomValueScRsp : BasePacket
{
    public PacketGetMainMissionCustomValueScRsp(GetMainMissionCustomValueCsReq req, PlayerInstance player) : base(
        CmdIds.GetMainMissionCustomValueScRsp)
    {
        var proto = new GetMainMissionCustomValueScRsp();
        foreach (var mission in req.MainMissionIdList)
            proto.MainMissionList.Add(player.MissionManager!.MainMissionToProto((int)mission));

        SetData(proto);
    }
}
