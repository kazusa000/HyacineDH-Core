using System.Collections.Concurrent;
using HyacineCore.Server.Data;
using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Data.Config.Scene;
using HyacineCore.Server.Data.Config.Task;
using HyacineCore.Server.Data.Excel;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.Enums.Scene;
using HyacineCore.Server.Enums.Task;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.GameServer.Game.Scene.Entity;
using HyacineCore.Server.Proto;
using HyacineCore.Server.Util;
using Newtonsoft.Json.Linq;

namespace HyacineCore.Server.GameServer.Game.Task;

public class LevelTask(PlayerInstance player)
{
    public PlayerInstance Player { get; } = player;

    #region Prop Target

    public async ValueTask<object?> TargetFetchAdvPropEx(TargetEvaluator act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        await ValueTask.CompletedTask;

        if (act is TargetFetchAdvPropEx fetch)
        {
            if (fetch.FetchType != TargetFetchAdvPropFetchTypeEnum.SinglePropByPropID) return null;
            foreach (var entity in Player.SceneInstance?.Entities.Values.ToList() ?? [])
                if (entity is EntityProp prop && prop.GroupId == fetch.SinglePropID.GroupID.GetValue() &&
                    prop.InstId == fetch.SinglePropID.ID.GetValue())
                    return prop;
        }

        return null;
    }

    #endregion

    #region Manage

    public void TriggerInitAct(LevelInitSequeceConfigInfo act, SubMissionData subMission, GroupInfo? group = null)
    {
        foreach (var task in act.TaskList) TriggerTask(task, subMission, group);
    }

    public void TriggerStartAct(LevelStartSequeceConfigInfo act, SubMissionData subMission, GroupInfo? group = null)
    {
        foreach (var task in act.TaskList) TriggerTask(task, subMission, group);
    }

    private void TriggerTask(TaskConfigInfo act, SubMissionData subMission, GroupInfo? group = null)
    {
        try
        {
            var methodName = act.Type.Replace("RPG.GameCore.", "");

            var method = GetOrCreateExecuteTask(methodName);
            if (method != null) _ = method(act, subMission, group);
        }
        catch
        {
        }
    }

    private ExecuteTask? GetOrCreateExecuteTask(string methodName)
    {
        // try to get from cache
        if (_cachedTasks.TryGetValue(methodName, out var method)) return method;
        var methodProp = GetType().GetMethod(methodName);
        if (methodProp == null) return null;

        method = (ExecuteTask)Delegate.CreateDelegate(typeof(ExecuteTask), this, methodProp);
        _cachedTasks[methodName] = method; // cached

        return method;
    }

    private delegate ValueTask<object?> ExecuteTask(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null);

    private readonly ConcurrentDictionary<string, ExecuteTask> _cachedTasks = [];

    #endregion

    #region Task

    public async ValueTask<object?> PlayMessage(TaskConfigInfo act, SubMissionData subMission, GroupInfo? group = null)
    {
        if (act is PlayMessage message) await Player.MessageManager!.AddMessageSection(message.MessageSectionID);

        return null;
    }

    public async ValueTask<object?> DestroyProp(TaskConfigInfo act, SubMissionData subMission, GroupInfo? group = null)
    {
        if (act is DestroyProp destroyProp)
            foreach (var entity in Player.SceneInstance!.Entities.Values)
                if (entity is EntityProp prop && prop.GroupId == destroyProp.GroupID.GetValue() &&
                    prop.InstId == destroyProp.ID.GetValue())
                    await Player.SceneInstance.RemoveEntity(entity);

        return null;
    }

    public async ValueTask<object?> TriggerCustomString(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (act is TriggerCustomString triggerCustomString)
        {
            foreach (var groupInfo in Player.SceneInstance?.FloorInfo?.Groups ?? [])
                if (groupInfo.Value.PropTriggerCustomString.TryGetValue(triggerCustomString.CustomString.Value,
                        out var list))
                    foreach (var id in list)
                    foreach (var entity in Player.SceneInstance?.Entities.Values.ToList() ?? [])
                        if (entity is EntityProp prop && prop.GroupId == groupInfo.Key && prop.InstId == id)
                            await prop.SetState(PropStateEnum.Closed);

            await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.PropState);
        }

        return null;
    }

    public async ValueTask<object?> EnterMap(TaskConfigInfo act, SubMissionData subMission, GroupInfo? group = null)
    {
        if (act is EnterMap enterMap)
            await Player.EnterSceneByEntranceId(enterMap.EntranceID, enterMap.GroupID, enterMap.AnchorID, true);

        return null;
    }

    public async ValueTask<object?> EnterMapByCondition(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (act is EnterMapByCondition enterMapByCondition)
            await Player.EnterSceneByEntranceId(enterMapByCondition.EntranceID.GetValue(), 0, 0, true);

        return null;
    }

    public async ValueTask<object?> TriggerPerformance(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (act is TriggerPerformance triggerPerformance)
        {
            if (triggerPerformance.PerformanceType == ELevelPerformanceTypeEnum.E)
                Player.TaskManager?.PerformanceTrigger.TriggerPerformanceE(triggerPerformance.PerformanceID,
                    subMission);
            else if (triggerPerformance.PerformanceType == ELevelPerformanceTypeEnum.D)
                Player.TaskManager?.PerformanceTrigger.TriggerPerformanceD(triggerPerformance.PerformanceID,
                    subMission);
        }

        await ValueTask.CompletedTask;

        return null;
    }

    public async ValueTask<object?> PredicateTaskList(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (act is PredicateTaskList predicateTaskList)
        {
            // handle predicateCondition
            var methodName = predicateTaskList.Predicate.Type.Replace("RPG.GameCore.", "");

            var method = GetOrCreateExecuteTask(methodName);
            if (method == null) return null;

            var resp = await method(predicateTaskList.Predicate, subMission, group);
            if (resp is true)
                foreach (var task in predicateTaskList.SuccessTaskList)
                    TriggerTask(task, subMission, group);
            else
                foreach (var task in predicateTaskList.FailedTaskList)
                    TriggerTask(task, subMission, group);
        }

        return null;
    }

    public async ValueTask<object?> ChangePropState(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (subMission.SubMissionInfo?.FinishType != MissionFinishTypeEnum.PropState)
        {
            await UnlockMissionProp(subMission);
            return null;
        }

        foreach (var entity in Player.SceneInstance!.Entities.Values)
            if (entity is EntityProp prop && prop.GroupId == subMission.SubMissionInfo.ParamInt1 &&
                prop.InstId == subMission.SubMissionInfo.ParamInt2)
                try
                {
                    if (prop.Excel.PropStateList.Contains(PropStateEnum.Closed))
                    {
                        await prop.SetState(PropStateEnum.Closed);
                    }
                    else
                    {
                        await prop.SetState(
                            prop.Excel.PropStateList[prop.Excel.PropStateList.IndexOf(prop.State) + 1]);

                        // Elevator
                        foreach (var id in prop.PropInfo.UnlockControllerID)
                        foreach (var entity2 in Player.SceneInstance!.Entities.Values)
                            if (entity2 is EntityProp prop2 && prop2.GroupId == id.Key &&
                                id.Value.Contains(prop2.InstId))
                                await prop2.SetState(PropStateEnum.Closed);
                    }
                }
                catch
                {
                    // ignored
                }

        return null;
    }

    public async ValueTask<object?> CreateTrialPlayer(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatar)
            await Player.LineupManager!.AddAvatarToCurTeam(subMission.SubMissionInfo.ParamInt1);

        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatarList)
            subMission.SubMissionInfo.ParamIntList?.ForEach(
                async x => await Player.LineupManager!.AddAvatarToCurTeam(x));

        return null;
    }

    public async ValueTask<object?> ReplaceTrialPlayer(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatar)
        {
            var ids = Player.LineupManager!.GetCurLineup()?.BaseAvatars?.ToList() ?? [];
            ids.ForEach(async x => await Player.LineupManager!.RemoveAvatarFromCurTeam(x.BaseAvatarId, false));
            await Player.LineupManager!.AddAvatarToCurTeam(subMission.SubMissionInfo.ParamInt1);
        }

        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatarList)
        {
            var ids = Player.LineupManager!.GetCurLineup()?.BaseAvatars?.ToList() ?? [];
            ids.ForEach(async x => await Player.LineupManager!.RemoveAvatarFromCurTeam(x.BaseAvatarId, false));
            subMission.SubMissionInfo.ParamIntList?.ForEach(
                async x => await Player.LineupManager!.AddAvatarToCurTeam(x));
        }

        return null;
    }

    public async ValueTask<object?> StoryLineReplaceTrialPlayer(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.StoryLineAddTrialAvatar)
        {
            var ids = Player.LineupManager!.GetCurLineup()?.BaseAvatars?.ToList() ?? [];
            ids.ForEach(async void (x) => await Player.LineupManager!.RemoveAvatarFromCurTeam(x.BaseAvatarId, false));
            await Player.LineupManager!.AddAvatarToCurTeam(subMission.SubMissionInfo.ParamInt1);
        }

        return null;
    }

    public async ValueTask<object?> ReplaceVirtualTeam(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (Player.LineupManager!.GetCurLineup()?.IsExtraLineup() != true) return null;

        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatar)
        {
            var ids = Player.LineupManager!.GetCurLineup()?.BaseAvatars?.ToList() ?? [];
            ids.ForEach(async x => await Player.LineupManager!.RemoveAvatarFromCurTeam(x.BaseAvatarId, false));
            ;
            await Player.LineupManager!.AddAvatarToCurTeam(subMission.SubMissionInfo.ParamInt1);
        }

        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatarList)
        {
            var ids = Player.LineupManager!.GetCurLineup()?.BaseAvatars?.ToList() ?? [];
            ids.ForEach(async x => await Player.LineupManager!.RemoveAvatarFromCurTeam(x.BaseAvatarId, false));
            subMission.SubMissionInfo.ParamIntList?.ForEach(
                async x => await Player.LineupManager!.AddAvatarToCurTeam(x));
        }

        return null;
    }

    public async ValueTask<object?> CreateHeroTrialPlayer(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatar)
            await Player.LineupManager!.AddAvatarToCurTeam(subMission.SubMissionInfo.ParamInt1);

        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatarList)
        {
            List<int> list = [.. subMission.SubMissionInfo.ParamIntList ?? []];

            if (list.Count > 0)
            {
                if (Player.Data.CurrentGender == Gender.Man)
                {
                    foreach (var avatar in subMission.SubMissionInfo?.ParamIntList ?? [])
                        if (avatar > 10000) // else is Base Avatar
                            if (avatar.ToString().EndsWith("8002") ||
                                avatar.ToString().EndsWith("8004") ||
                                avatar.ToString().EndsWith("8006"))
                                list.Remove(avatar);
                }
                else
                {
                    foreach (var avatar in subMission.SubMissionInfo?.ParamIntList ?? [])
                        if (avatar > 10000) // else is Base Avatar
                            if (avatar.ToString().EndsWith("8001") ||
                                avatar.ToString().EndsWith("8003") ||
                                avatar.ToString().EndsWith("8005"))
                                list.Remove(avatar);
                }
            }

            list.ForEach(async x => await Player.LineupManager!.AddAvatarToCurTeam(x));
        }

        return null;
    }

    public async ValueTask<object?> DestroyTrialPlayer(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.DelTrialAvatar)
            await Player.LineupManager!.RemoveAvatarFromCurTeam(subMission.SubMissionInfo.ParamInt1);

        return null;
    }

    // ChangePropState is a server-side task, so this resource data keeps it without target or state. Outside a PropState
    // sub-mission (handled above with its own target) it unlocks the Locked prop the story goes on with: one the
    // sub-mission's own graph refers to (101030104: the console it then waits on), or else the target of a PropState
    // sub-mission of the same main mission that is running or follows this one, the one nearest to the monster of a
    // KillMonster prerequisite or else to the player (1011301: each power core opens once its guard is beaten). The prop
    // goes to the state the player's interaction starts from: Open when that sub-mission waits for Closed (the core's
    // Open branch plays the performance and requests the interaction that closes it), otherwise Closed.
    private async ValueTask UnlockMissionProp(SubMissionData subMission)
    {
        var info = subMission.SubMissionInfo;
        var scene = Player.SceneInstance;
        if (info == null || scene == null) return;
        var locked = scene.Entities.Values.OfType<EntityProp>().Where(p => p.State == PropStateEnum.Locked).ToList();
        if (locked.Count == 0) return;

        var running = Player.MissionManager!.GetRunningSubMissionIdList();
        var goals = new Dictionary<(int, int), PropStateEnum>();
        foreach (var other in subMission.MainMissionInfo?.SubMissionList ?? [])
            if (other.FinishType == MissionFinishTypeEnum.PropState &&
                (running.Contains(other.ID) || other.TakeParamIntList?.Contains(info.ID) == true))
                goals.TryAdd((other.ParamInt1, other.ParamInt2), (PropStateEnum)other.ParamInt3);

        var own = GraphPropTargets(info);
        var targets = locked.Where(p => own.Contains((p.GroupId, p.InstId))).ToList();
        if (targets.Count == 0)
        {
            var near = KillMonsterPosition(info) ?? Player.Data.Pos;
            var candidates = locked.Where(p => goals.ContainsKey((p.GroupId, p.InstId))).ToList();
            var best = near == null
                ? candidates.FirstOrDefault()
                : candidates.MinBy(p => p.Position.GetFast2dDist(near));
            if (best != null) targets.Add(best);
        }

        foreach (var prop in targets)
        {
            var state = goals.TryGetValue((prop.GroupId, prop.InstId), out var goal) && goal == PropStateEnum.Closed &&
                        prop.Excel.PropStateList.Contains(PropStateEnum.Open)
                ? PropStateEnum.Open
                : PropStateEnum.Closed;
            if (!prop.Excel.PropStateList.Contains(state)) continue;
            Logger.GetByClassName().Info(
                $"Sub-mission {info.ID} ChangePropState: prop {prop.GroupId}/{prop.InstId} Locked -> {state}");
            await prop.SetState(state);
        }

        if (targets.Count > 0) await Player.MissionManager.HandleFinishType(MissionFinishTypeEnum.PropState);
    }

    // The position of the monster a KillMonster prerequisite of the sub-mission was about, if it is on this floor.
    private Position? KillMonsterPosition(SubMissionInfo info)
    {
        foreach (var id in info.TakeParamIntList ?? [])
        {
            GameData.SubMissionInfoData.TryGetValue(id, out var before);
            var kill = before?.SubMissionInfo;
            if (kill?.FinishType != MissionFinishTypeEnum.KillMonster) continue;
            if (Player.SceneInstance?.FloorInfo?.Groups.TryGetValue(kill.ParamInt1, out var group) != true) continue;
            var monster = group!.MonsterList.Find(m => m.ID == kill.ParamInt2);
            if (monster != null) return monster.ToPositionProto();
        }

        return null;
    }

    private static readonly ConcurrentDictionary<int, HashSet<(int, int)>> GraphPropTargetCache = [];

    // Props a sub-mission's level graph refers to by group and ID (TargetFetchAdvPropEx SinglePropByPropID), read from
    // the raw graph: its typed form keeps the nested callbacks' targets only where their task types are modelled.
    private static HashSet<(int, int)> GraphPropTargets(SubMissionInfo info)
    {
        return GraphPropTargetCache.GetOrAdd(info.ID, _ =>
        {
            var found = new HashSet<(int, int)>();
            var file = new FileInfo(ConfigManager.Config.Path.ResourcePath + "/" + info.MissionJsonPath);
            if (info.MissionJsonPath == "" || !file.Exists) return found;
            try
            {
                foreach (var token in JObject.Parse(File.ReadAllText(file.FullName)).Descendants().OfType<JObject>())
                {
                    if ((string?)token["$type"] != "RPG.GameCore.TargetFetchAdvPropEx" ||
                        (string?)token["FetchType"] != "SinglePropByPropID") continue;
                    var group = token.SelectToken("SinglePropID.GroupID.FixedValue.Value");
                    var prop = token.SelectToken("SinglePropID.ID.FixedValue.Value");
                    if (group != null && prop != null) found.Add(((int)group, (int)prop));
                }
            }
            catch
            {
                // an unreadable graph refers to no props
            }

            return found;
        });
    }

    public async ValueTask<object?> ChangeGroupState(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (group != null)
            foreach (var entity in Player.SceneInstance?.Entities.Values.ToList() ?? [])
                if (entity is EntityProp prop && prop.GroupId == group.Id)
                    if (prop.Excel.PropStateList.Contains(PropStateEnum.Open))
                        await prop.SetState(PropStateEnum.Open);

        return null;
    }

    public async ValueTask<object?> TriggerEntityServerEvent(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (group != null)
            foreach (var entity in Player.SceneInstance?.Entities.Values.ToList() ?? [])
                if (entity is EntityProp prop && prop.GroupId == group.Id)
                    if (prop.Excel.PropStateList.Contains(PropStateEnum.Open) &&
                        (prop.State == PropStateEnum.Closed || prop.State == PropStateEnum.Locked))
                        await prop.SetState(PropStateEnum.Open);

        return null;
    }

    // A prop in an event state (EventClose/EventOpen) is moved by the client's own interaction with it. The server runs
    // a group graph's tasks without its waits, so an event the graph sends after the player's interaction ran as soon as
    // the group loaded: 103030217's origami bird (Tpl_OrigamiBird G116, Clt_TriggerPullSuccessHaveCamera) went from
    // EventClose to Closed and lost its interaction.
    public async ValueTask<object?> TriggerEntityEvent(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (act is TriggerEntityEvent triggerEntityEvent)
            if (group != null)
                foreach (var entity in Player.SceneInstance?.Entities.Values.ToList() ?? [])
                    if (entity is EntityProp prop && prop.GroupId == group.Id &&
                        prop.InstId == triggerEntityEvent.InstanceID.GetValue())
                        if (prop.Excel.PropStateList.Contains(PropStateEnum.Closed) &&
                            prop.State is not (PropStateEnum.EventClose or PropStateEnum.EventOpen))
                            await prop.SetState(PropStateEnum.Closed);

        return null;
    }

    public async ValueTask<object?> PropSetupUITrigger(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (act is PropSetupUITrigger propSetupUiTrigger)
            foreach (var task in propSetupUiTrigger.ButtonCallback)
                TriggerTask(task, subMission, group);

        await ValueTask.CompletedTask;

        return null;
    }

    public async ValueTask<object?> PropStateExecute(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (act is PropStateExecute propStateExecute)
        {
            // handle targetType
            var methodName = propStateExecute.TargetType.Type.Replace("RPG.GameCore.", "");

            var method = GetType().GetMethod(methodName);
            if (method != null)
            {
                var resp = method.Invoke(this, [propStateExecute.TargetType, subMission, group]);
                if (resp is EntityProp result) await result.SetState(propStateExecute.State);
            }
        }

        return null;
    }

    #endregion

    #region Task Condition

    public async ValueTask<object?> ByCompareSubMissionState(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        await ValueTask.CompletedTask;

        if (act is ByCompareSubMissionState compare)
        {
            var mission = Player.MissionManager!.GetSubMissionStatus(compare.SubMissionID);
            return mission.ToStateEnum() == compare.SubMissionState;
        }

        return false;
    }

    public async ValueTask<object?> ByCompareFloorSavedValue(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        await ValueTask.CompletedTask;

        if (act is ByCompareFloorSavedValue compare)
        {
            var value = Player.SceneData!.FloorSavedData.GetValueOrDefault(Player.Data.FloorId, []);
            return compare.CompareType switch
            {
                CompareTypeEnum.Equal => value.GetValueOrDefault(compare.Name, 0) == compare.CompareValue,
                CompareTypeEnum.Greater => value.GetValueOrDefault(compare.Name, 0) > compare.CompareValue,
                CompareTypeEnum.Less => value.GetValueOrDefault(compare.Name, 0) < compare.CompareValue,
                CompareTypeEnum.GreaterEqual => value.GetValueOrDefault(compare.Name, 0) >= compare.CompareValue,
                CompareTypeEnum.LessEqual => value.GetValueOrDefault(compare.Name, 0) <= compare.CompareValue,
                CompareTypeEnum.NotEqual => value.GetValueOrDefault(compare.Name, 0) != compare.CompareValue,
                _ => false
            };
        }

        return false;
    }

    #endregion
}