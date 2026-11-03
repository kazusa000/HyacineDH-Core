using HyacineCore.Server.Data;
using HyacineCore.Server.Data.Config.Scene;
using HyacineCore.Server.Database.Scene;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.Enums.Scene;
using HyacineCore.Server.GameServer.Game.Drop;
using HyacineCore.Server.GameServer.Game.Mission;
using HyacineCore.Server.GameServer.Game.Scene;
using HyacineCore.Server.GameServer.Game.Scene.Entity;
using HyacineCore.Server.GameServer.Server.Packet.Send.Lineup;
using HyacineCore.Server.GameServer.Server.Packet.Send.MarkChest;
using HyacineCore.Server.GameServer.Server.Packet.Send.Scene;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;
using HyacineCore.Server.Util;
using static HyacineCore.Server.GameServer.Plugin.Event.PluginEvent;

namespace HyacineCore.Server.GameServer.Game.Player;

public partial class PlayerInstance
{
    #region Scene Actions

    public async ValueTask OnMove()
    {
        if (SceneInstance != null)
        {
            var prop = SceneInstance.GetNearestSpring(25_000_000);

            var isInRange = prop != null;

            if (isInRange)
                if (LineupManager?.GetCurLineup()?.Heal(10000, true) == true)
                    await SendPacket(new PacketSyncLineupNotify(LineupManager.GetCurLineup()!));
        }
    }

    public async ValueTask<EntityProp?> InteractProp(int propEntityId, int interactId)
    {
        if (SceneInstance == null) return null;
        SceneInstance.Entities.TryGetValue(propEntityId, out var entity);
        if (entity is not EntityProp prop) return null;
        if (TavernGlass.IsGlass(prop))
        {
            await TavernGlass.OnInteract(prop, interactId);
            return prop;
        }

        if (TavernStellaJade.IsJade(prop))
        {
            await TavernStellaJade.OnInteract(prop, interactId);
            return prop;
        }

        GameData.InteractConfigData.TryGetValue(interactId, out var config);
        if (config == null || config.SrcState != prop.State)
        {
            await PuzzleFallback.OnGroupInteract(this, prop.GroupId);
            return prop;
        }
        var oldState = prop.State;
        await prop.SetState(config.TargetState);
        var newState = prop.State;
        await SendPacket(new PacketGroupStateChangeScNotify(Data.EntryId, prop.GroupId, prop.State));

        switch (prop.Excel.PropType)
        {
            case PropTypeEnum.PROP_TREASURE_CHEST:
                if (oldState == PropStateEnum.ChestClosed && newState == PropStateEnum.ChestUsed)
                {
                    // TODO: Filter treasure chest
                    var items = DropService.CalculateDropsFromProp(prop.PropInfo.ChestID);
                    await InventoryManager!.AddItems(items);
                    await SendPacket(new PacketOpenChestScNotify(prop.PropInfo.ChestID));

                    var notifyMark = false;
                    foreach (var markedChest in SceneData!.MarkedChestData.Values)
                    {
                        var chest = markedChest.Find(x =>
                            x.FloorId == SceneInstance.FloorId && x.GroupId == prop.GroupId &&
                            x.ConfigId == prop.PropInfo.ID);

                        if (chest == null) continue;
                        markedChest.Remove(chest);
                        notifyMark = true;
                    }

                    if (notifyMark)
                        await SendPacket(new PacketMarkChestChangedScNotify(this));
                }

                break;
            case PropTypeEnum.PROP_DESTRUCT:
                if (newState == PropStateEnum.Closed) await prop.SetState(PropStateEnum.Open);
                break;
            case PropTypeEnum.PROP_MAZE_JIGSAW:
                switch (newState)
                {
                    case PropStateEnum.Closed:
                    {
                        foreach (var p in SceneInstance.GetEntitiesInGroup<EntityProp>(prop.GroupId))
                            if (p.Excel.PropType == PropTypeEnum.PROP_TREASURE_CHEST)
                            {
                                await p.SetState(PropStateEnum.ChestClosed);
                            }
                            else if (p.Excel.PropType == prop.Excel.PropType)
                            {
                                // Skip
                            }
                            else
                            {
                                await p.SetState(PropStateEnum.Open);
                            }

                        break;
                    }
                    case PropStateEnum.Open:
                    {
                        // The pieces keep their own states (hidden until their mission or the client shows them,
                        // collected when picked up): opening them here would leave an unused one (PieceCount)
                        // short of the Closed the board's group graph waits for.
                        foreach (var p in SceneInstance.GetEntitiesInGroup<EntityProp>(prop.GroupId).Where(p =>
                                     p.Excel.PropType is not PropTypeEnum.PROP_TREASURE_CHEST &&
                                     p.Excel.PropType != prop.Excel.PropType &&
                                     !p.PropInfo.Name.StartsWith("JigsawPiece")))
                            await p.SetState(PropStateEnum.Open);

                        await CheckJigsawPieces(prop.GroupId);
                        break;
                    }
                }

                break;
            case PropTypeEnum.PROP_MAZE_PUZZLE:
                if (newState is PropStateEnum.Closed or PropStateEnum.Open)
                    foreach (var p in SceneInstance.GetEntitiesInGroup<EntityProp>(prop.GroupId))
                    {
                        if (p.Excel.PropType == PropTypeEnum.PROP_TREASURE_CHEST)
                        {
                            await p.SetState(PropStateEnum.ChestClosed);
                        }
                        else if (p.Excel.PropType == prop.Excel.PropType)
                        {
                            // Skip
                        }
                        else
                        {
                            await p.SetState(PropStateEnum.Open);
                        }

                        await MissionManager!.OnPlayerInteractWithProp();
                    }

                break;
            case PropTypeEnum.PROP_ORDINARY:
                if (prop.PropInfo.CommonConsole)
                    // set group
                    foreach (var p in SceneInstance.GetEntitiesInGroup<EntityProp>(prop.GroupId))
                    {
                        await p.SetState(newState);

                        await MissionManager!.OnPlayerInteractWithProp();
                    }

                if (prop.Excel.ID == 104039)
                {
                    foreach (var p in SceneInstance.GetEntitiesInGroup<EntityProp>(prop.GroupId))
                        await p.SetState(newState);

                    await MissionManager!.OnPlayerInteractWithProp();
                }

                if (prop.PropInfo.Name.Contains("Piece")) await CheckJigsawPieces(prop.GroupId);

                break;
        }

        // Mission pieces are ordinary props (1036104's G277): collecting one can complete its board too.
        if (prop.Excel.PropType != PropTypeEnum.PROP_MAZE_PUZZLE && prop.PropInfo.Name.StartsWith("JigsawPiece"))
            await CheckJigsawPieces(prop.GroupId);

        // for door unlock. A door in a custom state (e.g. the Belobog fog door, CustomState01..05, one step per power
        // core) is advanced by the client's own interactions (101020..101023); opening it here would leave those
        // interactions, and the cores' graphs waiting on the door's state, without a match.
        if (prop.PropInfo.UnlockDoorID.Count > 0)
            foreach (var p in prop.PropInfo.UnlockDoorID.SelectMany(id =>
                         SceneInstance.GetEntitiesInGroup<EntityProp>(id.Key)
                             .Where(p => id.Value.Contains(p.PropInfo.ID) && p.State < PropStateEnum.CustomState01)))
            {
                await p.SetState(PropStateEnum.Open);
                await MissionManager!.OnPlayerInteractWithProp();
            }

        // for mission
        await MissionManager!.OnPlayerInteractWithProp();

        // plane event
        InventoryManager!.HandlePlaneEvent(prop.PropInfo.EventID);

        // handle plugin event
        InvokeOnPlayerInteract(this, prop);

        var floorSavedKey = prop.PropInfo.Name.Replace("Controller_", "");
        var key = $"FSV_ML{floorSavedKey}{(config.TargetState == PropStateEnum.Open ? "Started" : "Complete")}";

        if (prop.Group.GroupName.Contains("JigsawPuzzle") && prop.Group.GroupName.Contains("MainLine"))
        {
            var splits = prop.Group.GroupName.Split('_');
            key =
                $"JG_ML_{splits[3]}_Puzzle{(config.TargetState == PropStateEnum.Open ? "Started" : "Complete")}";
        }

        if (SceneInstance?.FloorInfo?.FloorSavedValue.Find(x => x.Name == key) != null)
        {
            // should save
            var plane = SceneInstance.PlaneId;
            var floor = SceneInstance.FloorId;
            SceneData!.FloorSavedData.TryGetValue(floor, out var value);
            if (value == null)
            {
                value = [];
                SceneData.FloorSavedData[floor] = value;
            }

            value[key] = 1; // ParamString[2] is the key
            await SendPacket(new PacketUpdateFloorSavedValueNotify(key, 1, this));

            TaskManager?.SceneTaskTrigger.TriggerFloor(plane, floor);
            MissionManager?.HandleFinishType(MissionFinishTypeEnum.FloorSavedValue);
        }

        if (prop.PropInfo.IsLevelBtn) await prop.SetState(PropStateEnum.Closed);

        // The tomb's Switch Hand socket locks after the placed core sends interaction 1120. Its client
        // graph plays the rune download at that point, but the mission waits for the server's Rune2 value.
        if (SceneInstance is { FloorId: 20462001 } tomb && prop.GroupId == 44 && prop.InstId == 300002 &&
            interactId == 1120 && oldState == PropStateEnum.Open && newState == PropStateEnum.Locked &&
            MissionManager!.GetSubMissionStatus(104365913) == MissionPhaseEnum.Accept &&
            tomb.GetGroupProperty(38, "Rune2") == 0)
            await tomb.UpdateGroupProperty(38, "Rune2", 1);

        await PuzzleFallback.OnGroupInteract(this, prop.GroupId);
        return prop;
    }

    // A jigsaw board (JigsawSir) offers its puzzle once the pieces it uses are collected: its group graph waits for
    // them Closed, then enables the board (TriggerEnable sets up the client's "enter puzzle" button). The board's
    // PieceCount (default 3) says how many of JigsawPiece1..3 it uses (1030402's second board, G173, takes two).
    // A locked board waits for its investigate interaction first (Locked -> Open), which sends the pieces out: the
    // client flies them off (TriggerPiecesFly) and the group graph turns the hidden ones (TriggerDisable) to
    // TriggerEnable, from where the pickup interaction (11000) collects them. Mission-controlled pieces (1030501's
    // G200) are picked up by the client once a mission group sends their custom string, so they need that state too.
    // Floor-saved-value boards and pieces (1030601's G372: UseFSV, UsePieceStateFSV) follow the values the missions
    // set instead, which the stripped group graph (WaitServerFloorSavedValueChange) would apply: the board's
    // UnlockWhenDisableFSVName unlocks it like its unlock custom string does (1920, TriggerDisable -> Locked, or
    // 19100 -> TriggerEnable for TriggerDisableType 0), and a piece's PieceStateFSVName collects it (Closed).
    public async ValueTask CheckJigsawPieces(int groupId, SceneInstance? scene = null)
    {
        scene ??= SceneInstance;
        if (scene == null) return;
        var props = scene.GetEntitiesInGroup<EntityProp>(groupId);
        SceneData!.FloorSavedData.TryGetValue(scene.FloorId, out var saved);
        bool FsvSet(EntityProp p, string flag, string name) =>
            Value<int>(p, flag) > 0 && Value<string>(p, name) is { Length: > 0 } key &&
            saved?.GetValueOrDefault(key) > 0;
        foreach (var piece in props.Where(p => p.PropInfo.Name.StartsWith("JigsawPiece") &&
                                               p.State != PropStateEnum.Closed &&
                                               FsvSet(p, "UsePieceStateFSV", "PieceStateFSVName")))
            await piece.SetState(PropStateEnum.Closed);
        foreach (var board in props.Where(p => p.PropInfo.Name.Contains("JigsawSir") &&
                                               p.State == PropStateEnum.TriggerDisable &&
                                               FsvSet(p, "UseFSV", "UnlockWhenDisableFSVName")))
            // TriggerDisableType defaults to 1 in the board's graph when the group leaves it out.
            await board.SetState(Value<float?>(board, "TriggerDisableType", 1) > 0.5
                ? PropStateEnum.Locked
                : PropStateEnum.TriggerEnable);

        // A locked board with no investigate interaction whose group graph enables it once its pieces are collected:
        // one predicate over all the pieces' states, then ChangePropState (1036104's mission board G277, pieces
        // 300002 and 300003, for 103610405). The stripped task's target is gone; the board goes to TriggerEnable,
        // where the client offers the puzzle, when those pieces are all Closed.
        if (scene.FloorInfo?.Groups.GetValueOrDefault(groupId) is { } info && PieceGate(info) is { Count: > 0 } gate &&
            gate.All(id => props.Any(p => p.PropInfo.ID == id && p.State == PropStateEnum.Closed)))
            foreach (var board in props.Where(p => p.PropInfo.Name.Contains("JigsawSir") &&
                                                   p.State == PropStateEnum.Locked))
                await board.SetState(PropStateEnum.TriggerEnable);

        if (props.Any(p => p.PropInfo.Name.Contains("JigsawSir") && p.State == PropStateEnum.Open))
            foreach (var piece in props.Where(p => p.PropInfo.Name.StartsWith("JigsawPiece") &&
                                                   p.State == PropStateEnum.TriggerDisable &&
                                                   Value<int>(p, "UsePieceStateFSV") <= 0))
                await piece.SetState(PropStateEnum.TriggerEnable);

        foreach (var board in props.Where(p => p.PropInfo.Name.Contains("JigsawSir") &&
                                               p.State is not (PropStateEnum.Closed or PropStateEnum.Locked or
                                                   PropStateEnum.TriggerEnable)))
        {
            var count = board.PropInfo.ValueSource?.Values.Find(x => x["Key"]?.ToString() == "PieceCount")?["Value"]
                ?.ToObject<int>() ?? 0;
            if (count <= 0) count = 3;
            var pieces = props.Where(p => p.PropInfo.Name.StartsWith("JigsawPiece") &&
                                          int.TryParse(p.PropInfo.Name["JigsawPiece".Length..], out var n) &&
                                          n <= count).ToList();
            if (pieces.Count > 0 && pieces.All(p => p.State == PropStateEnum.Closed))
                await board.SetState(PropStateEnum.TriggerEnable);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<int>> PieceGates = new();

    // The pieces a board's group graph checks together before it changes a prop's state (see CheckJigsawPieces).
    private static List<int> PieceGate(GroupInfo group) =>
        string.IsNullOrEmpty(group.LevelGraph)
            ? []
            : PieceGates.GetOrAdd(group.LevelGraph, path =>
            {
                try
                {
                    return Newtonsoft.Json.Linq.JToken.Parse(PuzzleFallback.ReadGraph(path)).SelectTokens("$..*")
                        .OfType<Newtonsoft.Json.Linq.JObject>()
                        .Where(t => (string?)t["$type"] == "RPG.GameCore.PredicateTaskList" &&
                                    t["SuccessTaskList"] is Newtonsoft.Json.Linq.JArray tasks &&
                                    tasks.Any(x => (string?)x["$type"] == "RPG.GameCore.ChangePropState"))
                        .Select(t => t["Predicate"]?.SelectTokens("$..Targets[*]").Select(x => (int)x).ToList() ?? [])
                        .FirstOrDefault(ids => ids.Count > 1 && ids.All(id =>
                            group.PropList.Any(p => p.ID == id && p.Name.StartsWith("JigsawPiece")))) ?? [];
                }
                catch
                {
                    // a graph that does not parse gates nothing
                    return [];
                }
            });

    // A prop's group-level graph value (a SharedFloat without a Value is 0); fallback when the group leaves it out.
    private static T? Value<T>(EntityProp prop, string key, T? fallback = default) =>
        prop.PropInfo.ValueSource?.Values.Find(x => x["Key"]?.ToString() == key) is { } entry
            ? entry["Value"] is { } value ? value.ToObject<T>() : default
            : fallback;

    // A door that reports a mission step by opening (1030701's AirLock01_1, G37: its Clt_OnOpen on AirLockDoor2 sets
    // MissionFCV_103070108, which the step's group waits for) is opened by the floor saved value the step before sets
    // (MissionFV_103070108). The group graph's server-side wait for that value (LoopWaitServerFloorSavedValueChange)
    // is stripped from the data, so the door is found by the custom value its open event sets.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<(string Value, int InstId)>>
        MissionDoors = new();

    private static List<(string Value, int InstId)> MissionDoorsOf(GroupInfo group) =>
        MissionDoors.GetOrAdd(group.LevelGraph, path =>
        {
            var doors = new List<(string, int)>();
            var file = Path.Combine(ConfigManager.Config.Path.ResourcePath, path);
            if (string.IsNullOrEmpty(path) || !File.Exists(file)) return doors;
            try
            {
                foreach (var wait in Newtonsoft.Json.Linq.JToken.Parse(File.ReadAllText(file))
                             .SelectTokens("$..[?(@.EventName.Value == 'Clt_OnOpen')]"))
                {
                    var inst = wait.SelectToken("InstanceID.FixedValue.Value")?.ToObject<int>() ?? 0;
                    foreach (var name in wait.SelectTokens("OnEvent[*].Name.Value"))
                        if (name.ToString() is { } fcv && fcv.StartsWith("MissionFCV_") && inst > 0)
                            doors.Add(("MissionFV_" + fcv["MissionFCV_".Length..], inst));
                }
            }
            catch
            {
                // a graph that does not parse opens nothing
            }

            return doors;
        });

    public async ValueTask OpenMissionDoors(GroupInfo group, SceneInstance? scene = null)
    {
        scene ??= SceneInstance;
        if (scene == null) return;
        SceneData!.FloorSavedData.TryGetValue(scene.FloorId, out var saved);
        foreach (var (value, instId) in MissionDoorsOf(group))
            if (saved?.GetValueOrDefault(value) > 0)
                foreach (var door in scene.GetEntitiesInGroup<EntityProp>(group.Id).Where(p =>
                             p.InstId == instId && p.State is PropStateEnum.Closed or PropStateEnum.Locked))
                    await door.SetState(PropStateEnum.Open);
    }

    public async ValueTask SetPropTimeline(int propEntityId, PropTimelineInfo info)
    {
        if (SceneInstance == null) return;
        SceneInstance.Entities.TryGetValue(propEntityId, out var entity);
        if (entity is not EntityProp prop) return;

        var data = new ScenePropTimelineData
        {
            BoolValue = info.TimelineBoolValue,
            ByteValue = info.TimelineByteValue.ToBase64()
        };

        // save to db
        SceneData!.PropTimelineData.TryGetValue(Data.FloorId, out var floorData);
        if (floorData == null)
        {
            floorData = new Dictionary<int, Dictionary<int, ScenePropTimelineData>>();
            SceneData.PropTimelineData[Data.FloorId] = floorData;
        }

        if (!floorData.ContainsKey(prop.GroupId))
            floorData[prop.GroupId] = new Dictionary<int, ScenePropTimelineData>();

        floorData[prop.GroupId][prop.PropInfo.ID] = data;

        prop.PropTimelineData = data;

        // handle mission / quest
        await MissionManager!.HandleFinishType(MissionFinishTypeEnum.TimeLineSetState);
        await MissionManager!.HandleFinishType(MissionFinishTypeEnum.TimeLineSetStateCnt);
    }

    public ScenePropTimelineData? GetScenePropTimelineData(int floorId, int groupId, int propId)
    {
        SceneData!.PropTimelineData.TryGetValue(floorId, out var floorData);
        if (floorData == null) return null;
        floorData.TryGetValue(groupId, out var groupData);
        if (groupData == null) return null;
        groupData.TryGetValue(propId, out var data);
        return data;
    }

    public async ValueTask<bool> EnterScene(int entryId, int teleportId, bool sendPacket, int storyLineId = 0,
        bool mapTp = false)
    {
        var beforeStoryLineId = StoryLineManager?.StoryLineData.CurStoryLineId;
        if (storyLineId != StoryLineManager?.StoryLineData.CurStoryLineId)
        {
            if (StoryLineManager != null)
                await StoryLineManager.EnterStoryLine(storyLineId, entryId == 0); // entryId == 0 -> teleport
            mapTp = false; // do not use mapTp when enter story line
        }

        GameData.MapEntranceData.TryGetValue(entryId, out var entrance);
        if (entrance == null) return false;

        GameData.GetFloorInfo(entrance.PlaneID, entrance.FloorID, out var floorInfo);

        var startGroup = entrance.StartGroupID;
        var startAnchor = entrance.StartAnchorID;

        if (teleportId != 0)
        {
            floorInfo.CachedTeleports.TryGetValue(teleportId, out var teleport);
            if (teleport != null)
            {
                startGroup = teleport.AnchorGroupID;
                startAnchor = teleport.AnchorID;
            }
        }
        else if (startAnchor == 0)
        {
            startGroup = floorInfo.StartGroupID;
            startAnchor = floorInfo.StartAnchorID;
        }

        var anchor = floorInfo.GetAnchorInfo(startGroup, startAnchor);

        await MissionManager!.HandleFinishType(MissionFinishTypeEnum.EnterMapByEntrance, entrance);

        var beforeEntryId = Data.EntryId;

        await LoadScene(entrance.PlaneID, entrance.FloorID, entryId, anchor!.ToPositionProto(),
            anchor.ToRotationProto(), sendPacket, mapTp);

        var afterEntryId = Data.EntryId;

        return beforeEntryId != afterEntryId ||
               beforeStoryLineId != storyLineId; // return true if entryId changed or story line changed
    }

    public async ValueTask EnterSceneByEntranceId(int entranceId, int anchorGroupId, int anchorId, bool sendPacket)
    {
        GameData.MapEntranceData.TryGetValue(entranceId, out var entrance);
        if (entrance == null) return;

        GameData.GetFloorInfo(entrance.PlaneID, entrance.FloorID, out var floorInfo);

        var startGroup = anchorGroupId == 0 ? entrance.StartGroupID : anchorGroupId;
        var startAnchor = anchorId == 0 ? entrance.StartAnchorID : anchorId;

        if (startAnchor == 0)
        {
            startGroup = floorInfo.StartGroupID;
            startAnchor = floorInfo.StartAnchorID;
        }

        var anchor = floorInfo.GetAnchorInfo(startGroup, startAnchor);

        await LoadScene(entrance.PlaneID, entrance.FloorID, entranceId, anchor!.ToPositionProto(),
            anchor.ToRotationProto(), sendPacket);
    }

    public async ValueTask MoveTo(Position position)
    {
        Data.Pos = position;
        await SendPacket(new PacketSceneEntityMoveScNotify(this));
    }

    public void MoveTo(EntityMotion motion)
    {
        Data.Pos = motion.Motion.Pos.ToPosition();
        Data.Rot = motion.Motion.Rot.ToPosition();
    }

    public async ValueTask MoveTo(Position pos, Position rot)
    {
        Data.Pos = pos;
        Data.Rot = rot;
        await SendPacket(new PacketSceneEntityMoveScNotify(this));
    }

    public async ValueTask LoadScene(int planeId, int floorId, int entryId, Position pos, Position rot, bool sendPacket,
        bool mapTp = false)
    {
        GameData.MazePlaneData.TryGetValue(planeId, out var plane);
        if (plane == null) return;

        if (plane.PlaneType == PlaneTypeEnum.Raid && RaidManager!.RaidData.CurRaidId == 0)
        {
            await EnterScene(2000101, 0, sendPacket);
            return;
        }

        // Challenge planes have both "lobby/selection" entrances and "in-battle" entrances.
        // Only block entering challenge planes when we're trying to enter an actual challenge stage without an instance.
        if (plane.PlaneType == PlaneTypeEnum.Challenge && ChallengeManager!.ChallengeInstance == null &&
            entryId is not GameConstants.CHALLENGE_ENTRANCE and
                not GameConstants.CHALLENGE_BOSS_ENTRANCE and
                not GameConstants.CHALLENGE_PEAK_ENTRANCE and
                not GameConstants.CHALLENGE_STORY_ENTRANCE)
        {
            await EnterScene(100000103, 0, sendPacket);
            return;
        }

        // TODO: Sanify check
        if (SceneInstance != null && SceneInstance.FloorId != floorId)
            new MapRotationManager(this).Deactivate();
        Data.Pos = pos;
        Data.Rot = rot;
        var notSendMove = true;
        if (planeId != Data.PlaneId || floorId != Data.FloorId || entryId != Data.EntryId || SceneInstance == null ||
            !mapTp)
        {
            if (SceneInstance != null)
                await SceneInstance.OnDestroy();
            SceneInstance instance = new(this, plane, floorId, entryId);
            InvokeOnPlayerLoadScene(this, instance);
            SceneInstance = instance;

            await instance.SyncLineup(true);
            Data.PlaneId = planeId;
            Data.FloorId = floorId;
            Data.EntryId = entryId;
        }
        else if (StoryLineManager?.StoryLineData.CurStoryLineId == 0 &&
                 mapTp) // only send move packet when not in story line and mapTp
        {
            notSendMove = false;
        }

        if (MissionManager != null)
            await MissionManager.OnPlayerChangeScene();

        Connection?.SendPacket(CmdIds.SyncServerSceneChangeNotify);
        if (sendPacket && notSendMove)
            await SendPacket(new PacketEnterSceneByServerScNotify(SceneInstance!));
        else if (sendPacket && !notSendMove) // send move packet
            await SendPacket(new PacketSceneEntityMoveScNotify(this));

        if (MissionManager != null)
        {
            await MissionManager.HandleFinishType(MissionFinishTypeEnum.EnterFloor);
            await MissionManager.HandleFinishType(MissionFinishTypeEnum.EnterPlane);
            await MissionManager.HandleFinishType(MissionFinishTypeEnum.NotInFloor);
            await MissionManager.HandleFinishType(MissionFinishTypeEnum.NotInPlane);
        }
    }

    public ScenePropData? GetScenePropData(int floorId, int groupId, int propId)
    {
        if (SceneData != null)
            if (SceneData.ScenePropData.TryGetValue(floorId, out var floorData))
                if (floorData.TryGetValue(groupId, out var groupData))
                {
                    var propData = groupData.Find(x => x.PropId == propId);
                    return propData;
                }

        return null;
    }

    public void SetScenePropData(int floorId, int groupId, int propId, PropStateEnum state)
    {
        if (SceneData != null)
        {
            if (!SceneData.ScenePropData.TryGetValue(floorId, out var floorData))
            {
                floorData = [];
                SceneData.ScenePropData.Add(floorId, floorData);
            }

            if (!floorData.TryGetValue(groupId, out var groupData))
            {
                groupData = [];
                floorData.Add(groupId, groupData);
            }

            var propData = groupData.Find(x => x.PropId == propId); // find prop data
            if (propData == null)
            {
                propData = new ScenePropData
                {
                    PropId = propId,
                    State = state
                };
                groupData.Add(propData);
            }
            else
            {
                propData.State = state;
            }
        }
    }

    public void EnterSection(int sectionId)
    {
        if (SceneInstance != null)
        {
            SceneData!.UnlockSectionIdList.TryGetValue(SceneInstance.FloorId, out var unlockList);
            if (unlockList == null)
            {
                unlockList = [sectionId];
                SceneData.UnlockSectionIdList.Add(SceneInstance.FloorId, unlockList);
            }
            else
            {
                SceneData.UnlockSectionIdList[SceneInstance.FloorId].Add(sectionId);
            }
        }
    }

    public void SetCustomSaveData(int entryId, int groupId, string data)
    {
        if (SceneData != null)
        {
            if (!SceneData.CustomSaveData.TryGetValue(entryId, out var entryData))
            {
                entryData = [];
                SceneData.CustomSaveData.Add(entryId, entryData);
            }

            entryData[groupId] = data;
        }
    }

    public async ValueTask ForceQuitBattle()
    {
        if (BattleInstance != null)
        {
            BattleInstance = null;
            await Connection!.SendPacket(CmdIds.QuitBattleScNotify);
        }
    }

    #endregion
}
