using HyacineCore.Server.Data;
using HyacineCore.Server.Database;
using HyacineCore.Server.Database.Lineup;
using HyacineCore.Server.Database.Quests;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.GameServer.Server.Packet.Send.Lineup;
using HyacineCore.Server.GameServer.Server.Packet.Send.StoryLine;
using HyacineCore.Server.Proto;
using HyacineCore.Server.Util;

namespace HyacineCore.Server.GameServer.Game.Mission;

public class StoryLineManager(PlayerInstance player) : BasePlayerManager(player)
{
    private static readonly Logger Log = new("StoryLine");

    public StoryLineData StoryLineData { get; set; } =
        DatabaseHelper.Instance!.GetInstanceOrCreateNew<StoryLineData>(player.Uid);

    public async ValueTask CheckIfEnterStoryLine()
    {
        if (StoryLineData.CurStoryLineId != 0) return;

        // Every story line begins when a sub-mission is taken (SubMissionTaken) and ends when one is finished.
        // Lines already started are either suspended in RunningStoryLines (ChangeStoryLine switches back to
        // them) or ended, so only lines never entered qualify.
        var missions = Player.MissionManager!;
        foreach (var storyLine in GameData.StoryLineData.Values)
        {
            if (missions.GetSubMissionStatus(storyLine.BeginCondition.Param) == MissionPhaseEnum.None) continue;
            if (missions.GetSubMissionStatus(storyLine.EndCondition.Param) == MissionPhaseEnum.Finish) continue;
            if (StoryLineData.RunningStoryLines.ContainsKey(storyLine.StoryLineID)) continue;
            if (EnteredByOwnAction(storyLine.StoryLineID)) continue;
            await InitStoryLine(storyLine.StoryLineID);
            return;
        }

        // A line another line switched away from is suspended. When no line is active and the suspended
        // line's show range is still open, the player is back in it.
        // A raid replaces the story line while it runs, so none resumes until the raid is left.
        if (Player.RaidManager?.RaidData.CurRaidId > 0) return;
        foreach (var storyLineId in StoryLineData.RunningStoryLines.Keys.ToList())
        {
            if (GameData.StoryLineData.GetValueOrDefault(storyLineId)?.ShowRange() is not { } range) continue;
            if (missions.GetSubMissionStatus(range.From) == MissionPhaseEnum.None) continue;
            if (missions.GetSubMissionStatus(range.To) == MissionPhaseEnum.Finish) continue;
            Log.Info($"resume story line {storyLineId}");
            await InitStoryLine(storyLineId);
            return;
        }
    }

    public async ValueTask InitStoryLine(int storyLineId, int entryId = 0, int anchorGroupId = 0, int anchorId = 0)
    {
        Log.Info($"enter story line {storyLineId}");
        if (StoryLineData.CurStoryLineId != 0) await FinishStoryLine(entryId, anchorGroupId, anchorId, false);
        GameData.StoryLineData.TryGetValue(storyLineId, out var storyExcel);
        if (storyExcel == null) return;
        StoryLineData.RunningStoryLines.TryGetValue(storyLineId, out var lineInfo);
        StoryLineData.OldEntryId = Player.Data.EntryId;
        StoryLineData.OldFloorId = Player.Data.FloorId;
        StoryLineData.OldPlaneId = Player.Data.PlaneId;
        StoryLineData.OldPos = Player.Data.Pos!;
        StoryLineData.OldRot = Player.Data.Rot!;

        var current = Player.LineupManager!.GetCurLineup()!.BaseAvatars!;
        var avatarList = TrialLineup(storyLineId, current) ?? AvatarIds(current);

        Player.LineupManager!.SetExtraLineup(ExtraLineupType.LineupHeliobus, avatarList);

        StoryLineData.CurStoryLineId = storyExcel.StoryLineID;
        await Player.SendPacket(new PacketStoryLineInfoScNotify(Player));
        await Player.SendPacket(new PacketSyncLineupNotify(Player.LineupManager!.GetCurLineup()!));
        if (entryId > 0)
        {
            await Player.EnterSceneByEntranceId(entryId, anchorGroupId, anchorId, true);
        }
        else
        {
            if (lineInfo == null)
                await Player.EnterSceneByEntranceId(storyExcel.InitEntranceID, storyExcel.InitGroupID,
                    storyExcel.InitAnchorID,
                    true);
            else
                await Player.LoadScene(lineInfo.SavedPlaneId, lineInfo.SavedFloorId, lineInfo.SavedEntryId,
                    lineInfo.SavedPos, lineInfo.SavedRot, true);
        }

        await Player.SendPacket(
            new PacketChangeStoryLineFinishScNotify(storyExcel.StoryLineID, ChangeStoryLineAction.FinishAction));


        var record = new StoryLineInfo
        {
            Lineup = Player.LineupManager!.GetCurLineup()!.BaseAvatars!,
            SavedEntryId = Player.Data.EntryId,
            SavedFloorId = Player.Data.FloorId,
            SavedPlaneId = Player.Data.PlaneId,
            SavedPos = Player.Data.Pos!,
            SavedRot = Player.Data.Rot!,
            StoryLineId = storyExcel.StoryLineID
        };

        StoryLineData.RunningStoryLines[storyExcel.StoryLineID] = record;
    }

    public async ValueTask EnterStoryLine(int storyLineId, bool tp = true)
    {
        if (StoryLineData.CurStoryLineId == storyLineId) return; // already in this story line

        if (storyLineId == 0) // leave story line
        {
            await LeaveStoryLine(tp);
            return;
        }

        StoryLineData.RunningStoryLines.TryGetValue(storyLineId, out var lineInfo);
        if (lineInfo == null) return;

        if (StoryLineData.CurStoryLineId == 0) // not in any story line
        {
            StoryLineData.OldEntryId = Player.Data.EntryId;
            StoryLineData.OldFloorId = Player.Data.FloorId;
            StoryLineData.OldPlaneId = Player.Data.PlaneId;
            StoryLineData.OldPos = Player.Data.Pos!;
            StoryLineData.OldRot = Player.Data.Rot!;
        }
        else // in another story line
        {
            await LeaveStoryLine(false);
        }

        Player.LineupManager!.SetExtraLineup(ExtraLineupType.LineupHeliobus,
            TrialLineup(storyLineId, lineInfo.Lineup) ?? AvatarIds(lineInfo.Lineup));

        StoryLineData.CurStoryLineId = lineInfo.StoryLineId;
        await Player.SendPacket(new PacketStoryLineInfoScNotify(Player));
        await Player.SendPacket(new PacketSyncLineupNotify(Player.LineupManager!.GetCurLineup()!));

        if (tp)
            await Player.LoadScene(lineInfo.SavedPlaneId, lineInfo.SavedFloorId, lineInfo.SavedEntryId,
                lineInfo.SavedPos, lineInfo.SavedRot, true);
        await Player.SendPacket(
            new PacketChangeStoryLineFinishScNotify(StoryLineData.CurStoryLineId, ChangeStoryLineAction.Client));
    }

    public async ValueTask LeaveStoryLine(bool tp)
    {
        if (StoryLineData.CurStoryLineId == 0) return;
        Log.Info($"leave story line {StoryLineData.CurStoryLineId}");

        GameData.StoryLineData.TryGetValue(StoryLineData.CurStoryLineId, out var storyExcel);
        if (storyExcel == null) return;

        var record = new StoryLineInfo
        {
            Lineup = Player.LineupManager!.GetCurLineup()!.BaseAvatars!,
            SavedEntryId = Player.Data.EntryId,
            SavedFloorId = Player.Data.FloorId,
            SavedPlaneId = Player.Data.PlaneId,
            SavedPos = Player.Data.Pos!,
            SavedRot = Player.Data.Rot!,
            StoryLineId = storyExcel.StoryLineID
        };

        // reset
        Player.LineupManager!.SetExtraLineup(ExtraLineupType.LineupNone, []);

        // save
        StoryLineData.RunningStoryLines[storyExcel.StoryLineID] = record;
        StoryLineData.CurStoryLineId = 0;

        await Player.SendPacket(new PacketStoryLineInfoScNotify(Player));
        await Player.SendPacket(new PacketSyncLineupNotify(Player.LineupManager!.GetCurLineup()!));

        if (tp)
        {
            await Player.LoadScene(StoryLineData.OldPlaneId, StoryLineData.OldFloorId, StoryLineData.OldEntryId,
                StoryLineData.OldPos, StoryLineData.OldRot, true);

            StoryLineData.OldPlaneId = 0;
            StoryLineData.OldEntryId = 0;
            StoryLineData.OldFloorId = 0;
            StoryLineData.OldPos = new Position();
            StoryLineData.OldRot = new Position();
        }

        await Player.SendPacket(new PacketChangeStoryLineFinishScNotify(0, ChangeStoryLineAction.None));
    }

    public async ValueTask
        CheckIfFinishStoryLine() // seems like a story line end with another ChangeStoryLine finish action that Params[0] = 0
    {
        if (StoryLineData.CurStoryLineId == 0) return;
        GameData.StoryLineData.TryGetValue(StoryLineData.CurStoryLineId, out var storyExcel);
        if (storyExcel == null) return;

        if (Player.MissionManager!.GetSubMissionStatus(storyExcel.EndCondition.Param) == MissionPhaseEnum.Finish)
            await FinishStoryLine();
    }

    public async ValueTask FinishStoryLine(int entryId = 0, int anchorGroupId = 0, int anchorId = 0, bool tp = true)
    {
        if (StoryLineData.CurStoryLineId == 0) return;
        Log.Info($"leave story line {StoryLineData.CurStoryLineId}");

        GameData.StoryLineData.TryGetValue(StoryLineData.CurStoryLineId, out var storyExcel);
        if (storyExcel == null) return;
        Player.LineupManager!.SetExtraLineup(ExtraLineupType.LineupNone, []);

        // delete old & reset
        if (Player.MissionManager!.GetSubMissionStatus(storyExcel.EndCondition.Param) == MissionPhaseEnum.Finish)
            StoryLineData.RunningStoryLines.Remove(StoryLineData.CurStoryLineId);
        else
            StoryLineData.RunningStoryLines[StoryLineData.CurStoryLineId] = new StoryLineInfo
            {
                Lineup = Player.LineupManager!.GetCurLineup()!.BaseAvatars!,
                SavedEntryId = Player.Data.EntryId,
                SavedFloorId = Player.Data.FloorId,
                SavedPlaneId = Player.Data.PlaneId,
                SavedPos = Player.Data.Pos!,
                SavedRot = Player.Data.Rot!,
                StoryLineId = StoryLineData.CurStoryLineId
            };
        StoryLineData.CurStoryLineId = 0;

        // where to return to, read before it is reset below
        var (oldPlaneId, oldFloorId, oldEntryId) =
            (StoryLineData.OldPlaneId, StoryLineData.OldFloorId, StoryLineData.OldEntryId);
        var (oldPos, oldRot) = (StoryLineData.OldPos, StoryLineData.OldRot);
        StoryLineData.OldPlaneId = 0;
        StoryLineData.OldEntryId = 0;
        StoryLineData.OldFloorId = 0;
        StoryLineData.OldPos = new Position();
        StoryLineData.OldRot = new Position();

        await Player.SendPacket(new PacketSyncLineupNotify(Player.LineupManager!.GetCurLineup()!));
        await Player.SendPacket(new PacketStoryLineInfoScNotify(Player));
        await Player.SendPacket(new PacketChangeStoryLineFinishScNotify(0, ChangeStoryLineAction.None));

        if (tp)
        {
            if (entryId > 0)
                await Player.EnterSceneByEntranceId(entryId, anchorGroupId, anchorId, true);
            else
                await Player.LoadScene(oldPlaneId, oldFloorId, oldEntryId, oldPos, oldRot, true);
        }
    }

    // A line whose beginning sub-mission enters it with its own ChangeStoryLine finish action (1031601: 103150117
    // shows message 1307600 on 20321001 first, then switches to Acheron) is entered by that action, not when the
    // sub-mission is taken. True while that sub-mission is still running.
    private bool EnteredByOwnAction(int storyLineId)
    {
        if (!GameData.StoryLineData.TryGetValue(storyLineId, out var storyLine)) return false;
        var subId = storyLine.BeginCondition.Param;
        if (Player.MissionManager!.GetSubMissionStatus(subId) != MissionPhaseEnum.Accept) return false;
        return Player.MissionManager.GetSubMissionInfo(subId)?.FinishActionList?.Any(action =>
            action.FinishActionType == FinishActionTypeEnum.ChangeStoryLine &&
            action.FinishActionPara.FirstOrDefault() == storyLineId) == true;
    }

    public async ValueTask OnLogin()
    {
        // A line entered as soon as its beginning sub-mission was taken (before EnteredByOwnAction) is left again,
        // back where the player was, so the sub-mission can play out and enter it itself.
        if (StoryLineData.CurStoryLineId is var early and not 0 && EnteredByOwnAction(early))
        {
            var (planeId, floorId, entryId) =
                (StoryLineData.OldPlaneId, StoryLineData.OldFloorId, StoryLineData.OldEntryId);
            var (pos, rot) = (StoryLineData.OldPos, StoryLineData.OldRot);
            Log.Info($"story line {early} was entered before its sub-mission entered it; leaving it");
            await FinishStoryLine(tp: false);
            StoryLineData.RunningStoryLines.Remove(early);
            if (floorId != 0)
            {
                Player.Data.PlaneId = planeId;
                Player.Data.FloorId = floorId;
                Player.Data.EntryId = entryId;
                Player.Data.Pos = pos;
                Player.Data.Rot = rot;
            }
        }

        // A line entered with the wrong team (before TrialLineup) gets its own team back.
        if (StoryLineData.CurStoryLineId != 0 &&
            Player.LineupManager!.GetCurLineup() is { BaseAvatars: { } avatars } lineup && lineup.IsExtraLineup() &&
            TrialLineup(StoryLineData.CurStoryLineId, avatars) is { } trial)
        {
            Log.Info($"story line {StoryLineData.CurStoryLineId} team: {string.Join(",", trial)}");
            Player.LineupManager.SetExtraLineup(ExtraLineupType.LineupHeliobus, trial);
        }

        await Player.SendPacket(new PacketStoryLineInfoScNotify(Player));
    }

    // A line with its own trial team (StroyLineTrialAvatarData: 1031101 is 1021304 alone) plays with that team, not
    // the one the player had: the client looks the line's trial avatars up for its team screen and fails every frame
    // (TeamEditFilter, GetTrialInstanceID on nil) when the lineup lacks them. Null when the team already has one.
    private static List<int>? TrialLineup(int storyLineId, List<LineupAvatarInfo> lineup)
    {
        if (!GameData.StroyLineTrialAvatarDataData.TryGetValue(storyLineId, out var trial) ||
            trial.InitTrialAvatarList.Count == 0) return null;
        if (lineup.Any(x => trial.TrialAvatarList.Contains(x.SpecialAvatarId))) return null;
        return trial.InitTrialAvatarList.ToList();
    }

    private static List<int> AvatarIds(List<LineupAvatarInfo> lineup)
    {
        return lineup.Select(x => x.SpecialAvatarId > 0 ? x.SpecialAvatarId : x.BaseAvatarId).ToList();
    }
}