using HyacineCore.Server.GameServer.Game.Mission;
using HyacineCore.Server.GameServer.Game.Scene;
using HyacineCore.Server.GameServer.Server.Packet.Send.Scene;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.Scene;

[Opcode(CmdIds.UpdateGroupPropertyCsReq)]
public class HandlerUpdateGroupPropertyCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = UpdateGroupPropertyCsReq.Parser.ParseFrom(data);

        if (req.FloorId != connection.Player!.SceneInstance!.FloorId)
        {
            await connection.SendPacket(new PacketUpdateGroupPropertyScRsp(Retcode.RetReqParaInvalid));
            return;
        }

        // try to get group
        var scene = connection.Player.SceneInstance;
        if (!scene.Groups.Contains((int)req.GroupId))
        {
            await connection.SendPacket(new PacketUpdateGroupPropertyScRsp(Retcode.RetGroupNotExist));
            return;
        }

        // update group property
        var res = await scene.UpdateGroupProperty((int)req.GroupId, req.NOCBONMOOGC, req.BFJLBKKJBLD);
        await connection.SendPacket(new PacketUpdateGroupPropertyScRsp(res, req));
        await PuzzleFallback.OnClientGroupProperty(connection.Player, (int)req.GroupId, req.NOCBONMOOGC,
            (int)req.BFJLBKKJBLD);
        await new EraFlipperManager(connection.Player).OnGroupProperty(req.NOCBONMOOGC, (int)req.BFJLBKKJBLD);
        await CorePlateArrival.OnGroupProperty(connection.Player, (int)req.GroupId, req.NOCBONMOOGC,
            (int)req.BFJLBKKJBLD);

        // The G90 flight sequence sends FakePlaneEnd only after its last shot. The 104370233 step waits for
        // the resulting floor saved value, which the client does not write directly.
        var player = connection.Player;
        if (scene == player.SceneInstance && scene.FloorId == 20482001 && req.GroupId == 90 &&
            req.NOCBONMOOGC == "FakePlaneEnd" && req.BFJLBKKJBLD == 1 &&
            player.StoryLineManager?.StoryLineData.CurStoryLineId == 1043702 &&
            player.MissionManager!.GetRunningSubMissionIdList().Contains(104370233) &&
            player.SceneData!.GetFloorSavedValues(scene.FloorId).GetValueOrDefault("FSV_TriggerFakePlane") == 1)
            await scene.UpdateFloorSavedValue("FSV_TriggerFakePlane", 2);
    }
}
