using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.Mission;

public class PacketGetMissionStatusScRsp : BasePacket
{
    public PacketGetMissionStatusScRsp(GetMissionStatusCsReq req, PlayerInstance player) : base(
        CmdIds.GetMissionStatusScRsp)
    {
        var proto = new GetMissionStatusScRsp();

        foreach (var item in req.MainMissionIdList)
        {
            proto.MainMissionMcvList.Add(new MainMissionCustomValue
            {
                MainMissionId = item,
                CustomValueList = new MissionCustomValueList
                {
                    CustomValueList = { player.MissionManager!.MainMissionToProto((int)item).CustomValueList }
                }
            });
            var status = player.MissionManager!.GetMainMissionStatus((int)item);
            if (status == MissionPhaseEnum.Finish)
                proto.FinishedMainMissionIdList.Add(item);
            else if (player.MissionManager!.Data.IsMainMissionDisabled((int)item))
                proto.DisabledMainMissionIdList.Add(item);
            else
                proto.UnfinishedMainMissionIdList.Add(item);
        }

        foreach (var item in req.SubMissionIdList)
        {
            var status = player.MissionManager!.GetSubMissionStatus((int)item);
            proto.SubMissionStatusList.Add(new Proto.Mission
            {
                Id = item,
                Status = status.ToProto()
            });
        }

        SetData(proto);
    }
}
