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
        if (scene == connection.Player.SceneInstance)
        {
            await GechuanMoonBoss.OnGroupProperty(scene, (int)req.GroupId, req.NOCBONMOOGC, (int)req.BFJLBKKJBLD);
            await TavernStellaJade.OnGroupProperty(scene, (int)req.GroupId, req.NOCBONMOOGC, (int)req.BFJLBKKJBLD);

            // The weapon stage sends this after all assembly lights turn green.
            if (req.GroupId == 7 && req.NOCBONMOOGC == "LG_110002_WeaponSetupDone" && req.BFJLBKKJBLD == 1)
                await scene.SyncRobotWorkshop();

            if (req.GroupId == 8 && req.BFJLBKKJBLD == 1 &&
                req.NOCBONMOOGC is "LG_110002__14_BombTriggerState_Auto" or
                    "LG_110002__10_BombTriggerState_Auto" or "LG_110002__11_BombTriggerState_Auto")
            {
                await scene.SyncWaterPipe();
                await PuzzleFallback.SyncGuidedWaterPipe(scene);
            }
        }

        // The G90 flight sequence sends FakePlaneEnd only after its last shot. The 104370233 step waits for
        // the resulting floor saved value, which the client does not write directly.
        var player = connection.Player;
        if (scene == player.SceneInstance && scene.FloorId == 20482001 && req.GroupId == 90 &&
            req.NOCBONMOOGC == "FakePlaneEnd" && req.BFJLBKKJBLD == 1 &&
            player.StoryLineManager?.StoryLineData.CurStoryLineId == 1043702 &&
            player.MissionManager!.GetRunningSubMissionIdList().Contains(104370233) &&
            player.SceneData!.GetFloorSavedValues(scene.FloorId).GetValueOrDefault("FSV_TriggerFakePlane") == 1)
            await scene.UpdateFloorSavedValue("FSV_TriggerFakePlane", 2);

        // The city fight starts after the native Ranger attack sequence sends this result.
        if (scene == player.SceneInstance && scene.FloorId == 20513001 && req.GroupId == 26 &&
            req.NOCBONMOOGC == "GP_Server_QTEDone" && req.BFJLBKKJBLD == 1 &&
            scene.GetGroupProperty(26, "GP_Server_QTEDone") == 1 &&
            scene.GetFloorSavedValue("FSV_HeroQTE_Done") == 0 &&
            player.MissionManager!.GetRunningSubMissionIdList().Contains(105410014))
            await scene.UpdateFloorSavedValue("FSV_HeroQTE_Done", 1);

        // Both Playing Rooms use this group property name. The group identifies the floor key, and the
        // current stage and running mission prevent a late or repeated notification from jumping ahead.
        if (scene == player.SceneInstance && scene.FloorId == 20481001 && req.NOCBONMOOGC == "SGP_PlayingRoom1Stage")
        {
            var stage = (int)req.BFJLBKKJBLD;
            var (roomStage, mission) = (req.GroupId, stage) switch
            {
                (39, 3) => ("FSV_PlayingRoom1Stage", 104370636),
                (39, 4) => ("FSV_PlayingRoom1Stage", 104370637),
                (48, 2) => ("FSV_PlayingRoom2Stage", 104370652),
                (48, 3) => ("FSV_PlayingRoom2Stage", 104370653),
                (48, 4) => ("FSV_PlayingRoom2Stage", 104370654),
                _ => ("", 0)
            };
            if (mission != 0 && player.MissionManager!.GetRunningSubMissionIdList().Contains(mission) &&
                player.SceneData!.GetFloorSavedValues(scene.FloorId).GetValueOrDefault(roomStage) == stage - 1)
                await scene.UpdateFloorSavedValue(roomStage, stage);
        }
    }
}
