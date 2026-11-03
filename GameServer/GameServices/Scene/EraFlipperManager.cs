using System.Text.RegularExpressions;
using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Mission;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.GameServer.Server.Packet;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;
using HyacineCore.Server.Util;
using Newtonsoft.Json.Linq;

namespace HyacineCore.Server.GameServer.Game.Scene;

// Era flips (floors with the EraFlipper feature) are played by the client's group graphs, which report each region's
// new state in ChangeEraFlipperDataCsReq; the reply carries the states back. The server task that counts a flip in a
// floor saved value is stripped: 1040102's G62 flips region 6 once Tribbie has led the way to the spot and then waits
// for FSV_Num_FB_Start_Temple to go from 1 to 2, the value 104010262 finishes on. So a flip sets a running FloorSavedValue
// step's value to its target when the value is one below it and a group graph of this floor that flips reads it.
// The flip can also come from an era device the graph hands control to (1040106's G55 "施展「奇迹」",
// EnterFlipperLightDeviceControl, which toggles region 6), while 104010627 waits for FSV_Era_Switched 0 -> 1.
public class EraFlipperManager(PlayerInstance player)
{
    private static readonly Logger Log = new("EraFlipper");

    public async ValueTask Change(ChangeEraFlipperDataCsReq req)
    {
        // Each region keeps its era across logins (1040106's scale room: its hand controllers only show in the
        // flipped era); GetEraFlipperData hands them back.
        foreach (var region in req.Data?.EGNOCAJJKIO ?? [])
            if (region.State == 0) player.SceneData!.EraFlipperData.RegionState.Remove((int)region.LDIJAKPKLIN);
            else player.SceneData!.EraFlipperData.RegionState[(int)region.LDIJAKPKLIN] = (int)region.State;
        var rsp = new BasePacket((ushort)CmdIds.ChangeEraFlipperDataScRsp);
        rsp.SetData(new ChangeEraFlipperDataScRsp { Data = req.Data ?? new LNMMKIBKCMP() });
        await player.SendPacket(rsp);
        if (req.Data?.EGNOCAJJKIO.Count > 0) await CountFlip();
    }

    public async ValueTask Get()
    {
        var data = new LNMMKIBKCMP();
        foreach (var (region, state) in player.SceneData!.EraFlipperData.RegionState)
            data.EGNOCAJJKIO.Add(new KPENFKEBGNE { LDIJAKPKLIN = (uint)region, State = (uint)state });
        var rsp = new BasePacket((ushort)CmdIds.GetEraFlipperDataScRsp);
        rsp.SetData(new GetEraFlipperDataScRsp { Data = data });
        await player.SendPacket(rsp);
    }

    private async ValueTask CountFlip()
    {
        if (player.SceneInstance is not { } scene || player.SceneData == null) return;
        var saved = player.SceneData.GetFloorSavedValues(scene.FloorId); // with the floor's defaults
        foreach (var info in player.MissionManager!.GetRunningSubMissionList())
        {
            if (info.LevelFloorID != scene.FloorId || info.FinishType != MissionFinishTypeEnum.FloorSavedValue ||
                string.IsNullOrEmpty(info.ParamStr1) || saved.GetValueOrDefault(info.ParamStr1) != info.ParamInt1 - 1)
                continue;
            // The Playing Rooms flip their lighting during load and after a crystal. Only the crystal's
            // group-property report advances these stages; a load-time flip would otherwise skip the crystal.
            if (scene.FloorId == 20481001 && info.ParamStr1 is ("FSV_PlayingRoom1Stage" or "FSV_PlayingRoom2Stage"))
                continue;
            var name = $"\"{info.ParamStr1}\"";
            if (!(scene.FloorInfo?.Groups.Values ?? []).Any(g => !string.IsNullOrEmpty(g.LevelGraph) &&
                                                                PuzzleFallback.ReadGraph(g.LevelGraph) is var text &&
                                                                text.Contains(name) &&
                                                                (text.Contains("\"RPG.GameCore.FlipEraRegion\"") ||
                                                                 text.Contains("\"RPG.GameCore.EnterFlipperLightDeviceControl\""))))
                continue;
            Log.Info($"Sub-mission {info.ID}: era flip on floor {scene.FloorId} sets {info.ParamStr1}={info.ParamInt1}");
            await scene.UpdateFloorSavedValue(info.ParamStr1, info.ParamInt1);
        }
    }

    // An era device's graph reports a finished flip as a group property (1040213's G39 Era_Device_Scaleroom:
    // Clt_EraFlipFinished sends EraFlipFinished_<n>=1, the second one only once FSV_CrossBlock is 1), and the stripped
    // server task turns it into the room's FSV_Era_Switched_ScaleRoom[_<n>]: G73 finishes 104021314 on the first, 104021356
    // waits for _2 and G158 finishes 104021361 on _3. A running step that waits for the n-th such value, itself or
    // through its group's graph, takes it.
    public async ValueTask OnGroupProperty(string name, int value)
    {
        var flip = Regex.Match(name, @"^EraFlipFinished_(\d+)$");
        if (!flip.Success || value == 0 || player.SceneInstance is not { } scene || player.SceneData == null) return;
        var n = int.Parse(flip.Groups[1].Value);
        var saved = player.SceneData.GetFloorSavedValues(scene.FloorId);
        foreach (var info in player.MissionManager!.GetRunningSubMissionList())
        {
            if (info.LevelFloorID != scene.FloorId) continue;
            foreach (var (key, target) in Waits(scene, info))
            {
                if (!key.StartsWith("FSV_Era_Switched") || saved.GetValueOrDefault(key) == target) continue;
                var index = Regex.Match(key, @"_(\d+)$");
                if ((index.Success ? int.Parse(index.Groups[1].Value) : 1) != n) continue;
                Log.Info($"Sub-mission {info.ID}: {name} on floor {scene.FloorId} sets {key}={target}");
                await scene.UpdateFloorSavedValue(key, target);
            }
        }
    }

    // The floor saved values a step waits for: its own FloorSavedValue condition, or a group graph branch that finishes
    // it (ClientFinishMission) once ByCompareFloorSavedValue holds.
    private static List<(string Key, int Value)> Waits(SceneInstance scene, SubMissionInfo info)
    {
        List<(string, int)> waits = [];
        if (info.FinishType == MissionFinishTypeEnum.FloorSavedValue && !string.IsNullOrEmpty(info.ParamStr1))
            waits.Add((info.ParamStr1, info.ParamInt1));
        if (info.FinishType != MissionFinishTypeEnum.Talk) return waits;
        foreach (var id in info.GroupIDList ?? [])
        {
            var graph = scene.FloorInfo?.Groups.GetValueOrDefault(id)?.LevelGraph;
            if (string.IsNullOrEmpty(graph)) continue;
            var text = PuzzleFallback.ReadGraph(graph);
            if (!text.Contains("RPG.GameCore.ClientFinishMission")) continue;
            try
            {
                foreach (var branch in JToken.Parse(text).SelectTokens("$..*").OfType<JObject>())
                {
                    var compare = branch["Predicate"] ?? branch["Condition"];
                    var tasks = branch["SuccessTaskList"] ?? branch["OnChange"];
                    if ((string?)compare?["$type"] != "RPG.GameCore.ByCompareFloorSavedValue" || tasks is not JArray list ||
                        compare["CompareValue"] == null || !list.Any(t =>
                            (string?)t["$type"] == "RPG.GameCore.ClientFinishMission" && (int?)t["SubmissionID"] == info.ID))
                        continue;
                    var key = (string?)compare["DynamicName"]?["Value"] ?? (string?)compare["Name"];
                    if (!string.IsNullOrEmpty(key)) waits.Add((key, (int)compare["CompareValue"]!));
                }
            }
            catch
            {
                // a graph that does not parse waits for nothing
            }
        }

        return waits.Distinct().ToList(); // a graph can wait for the value in more than one branch
    }
}
