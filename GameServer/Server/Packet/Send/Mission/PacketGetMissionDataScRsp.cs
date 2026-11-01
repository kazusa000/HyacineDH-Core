using HyacineCore.Server.Data;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.Mission;

public class PacketGetMissionDataScRsp : BasePacket
{
    public PacketGetMissionDataScRsp(PlayerInstance player) : base(CmdIds.GetMissionDataScRsp)
    {
        // A finished tracked mission hands over to the one its row names next (NextTrackMainMission): a client stopped
        // before it reported the change (1031001 -> 1031101) otherwise tracks the first running one, a side quest.
        var data = player.MissionManager!.Data;
        var next = data.TrackingMainMissionId;
        for (var hops = 0; hops < 16 && player.MissionManager.GetMainMissionStatus(next) == MissionPhaseEnum.Finish &&
                           GameData.MainMissionData.TryGetValue(next, out var row) && row.NextTrackMainMission > 0; hops++)
            next = row.NextTrackMainMission;
        if (player.MissionManager.GetMainMissionStatus(next) == MissionPhaseEnum.Accept)
            data.TrackingMainMissionId = next;

        // A finished or unset tracked mission falls back to a running one (as March7thHoney 4.2).
        if (player.MissionManager.GetMainMissionStatus(data.TrackingMainMissionId) != MissionPhaseEnum.Accept)
            data.TrackingMainMissionId = GameData.MainMissionData.Keys.FirstOrDefault(x =>
                player.MissionManager.GetMainMissionStatus(x) == MissionPhaseEnum.Accept);
        var proto = new GetMissionDataScRsp
        {
            TrackMissionId = (uint)data.TrackingMainMissionId
        };

        foreach (var mission in GameData.MainMissionData.Keys)
            if (player.MissionManager!.GetMainMissionStatus(mission) == MissionPhaseEnum.Accept)
                proto.MainMissionList.Add(player.MissionManager.MainMissionToProto(mission));

        foreach (var mission in GameData.SubMissionInfoData.Keys.Concat(GameData.SubMissionData.Keys))
            if (player.MissionManager!.GetSubMissionStatus(mission) == MissionPhaseEnum.Accept)
                proto.MissionList.Add(new Proto.Mission
                {
                    Id = (uint)mission,
                    Status = MissionStatus.MissionDoing,
                    Progress = (uint)player.MissionManager!.GetMissionProgress(mission)
                });

        SetData(proto);
    }
}
