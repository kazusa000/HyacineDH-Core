using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Mission;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.GameServer.Server.Packet;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;
using HyacineCore.Server.Util;

namespace HyacineCore.Server.GameServer.Game.Scene;

// Era flips (floors with the EraFlipper feature) are played by the client's group graphs, which report each region's
// new state in ChangeEraFlipperDataCsReq; the reply carries the states back. The server task that counts a flip in a
// floor saved value is stripped: 1040102's G62 flips region 6 once Tribbie has led the way to the spot and then waits
// for FSV_Num_FB_Start_Temple to go from 1 to 2, the value 104010262 finishes on. So a flip sets a running FloorSavedValue
// step's value to its target when the value is one below it and a group graph of this floor that flips reads it.
public class EraFlipperManager(PlayerInstance player)
{
    private static readonly Logger Log = new("EraFlipper");

    public async ValueTask Change(ChangeEraFlipperDataCsReq req)
    {
        var rsp = new BasePacket((ushort)CmdIds.ChangeEraFlipperDataScRsp);
        rsp.SetData(new ChangeEraFlipperDataScRsp { Data = req.Data ?? new LNMMKIBKCMP() });
        await player.SendPacket(rsp);
        if (req.Data?.EGNOCAJJKIO.Count > 0) await CountFlip();
    }

    private async ValueTask CountFlip()
    {
        if (player.SceneInstance is not { } scene || player.SceneData == null) return;
        player.SceneData.FloorSavedData.TryGetValue(scene.FloorId, out var saved);
        foreach (var info in player.MissionManager!.GetRunningSubMissionList())
        {
            if (info.LevelFloorID != scene.FloorId || info.FinishType != MissionFinishTypeEnum.FloorSavedValue ||
                string.IsNullOrEmpty(info.ParamStr1) || saved?.GetValueOrDefault(info.ParamStr1) != info.ParamInt1 - 1)
                continue;
            var name = $"\"{info.ParamStr1}\"";
            if (!(scene.FloorInfo?.Groups.Values ?? []).Any(g => !string.IsNullOrEmpty(g.LevelGraph) &&
                                                                PuzzleFallback.ReadGraph(g.LevelGraph) is var text &&
                                                                text.Contains(name) &&
                                                                text.Contains("\"RPG.GameCore.FlipEraRegion\"")))
                continue;
            Log.Info($"Sub-mission {info.ID}: era flip on floor {scene.FloorId} sets {info.ParamStr1}={info.ParamInt1}");
            await scene.UpdateFloorSavedValue(info.ParamStr1, info.ParamInt1);
        }
    }
}
