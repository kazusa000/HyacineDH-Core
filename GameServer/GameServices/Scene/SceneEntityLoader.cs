using HyacineCore.Server.Data;
using HyacineCore.Server.Data.Config.Scene;
using HyacineCore.Server.Enums;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.Enums.Scene;
using HyacineCore.Server.GameServer.Game.Scene.Entity;
using HyacineCore.Server.GameServer.Server.Packet.Send.Scene;
using HyacineCore.Server.Util;

namespace HyacineCore.Server.GameServer.Game.Scene;

public class SceneEntityLoader(SceneInstance scene)
{
    public SceneInstance Scene { get; set; } = scene;
    public List<int> LoadGroups { get; set; } = [];

    public virtual async ValueTask LoadEntity()
    {
        if (Scene.IsLoaded) return;

        var dimInfo = Scene.FloorInfo?.DimensionList.Find(x => x.ID == 0);
        if (dimInfo == null) return;
        LoadGroups.AddRange(dimInfo.GroupIDList);

        foreach (var group in from @group in Scene.FloorInfo?.Groups.Values!
                 where @group.LoadSide != GroupLoadSideEnum.Client
                 where !@group.GroupName.Contains("DeployPuzzle_Repeat_Area")
                 where !@group.GroupName.Contains("TrainVisitor")
                 where !@group.GroupName.Contains("TrainVisiter")
                 select @group) await LoadGroup(group);

        Scene.IsLoaded = true;
    }

    public virtual async ValueTask SyncEntity()
    {
        // Empty groups have client graphs/properties too; entity presence alone cannot track their lifecycle.
        var oldGroupId = Scene.Groups.Concat(Scene.Entities.Values.Select(e => e.GroupId)).ToHashSet();
        var loadedGroups = new List<int>();
        var unloadedGroups = new List<int>();

        foreach (var group in Scene.FloorInfo!.Groups.Values
                     .Where(group => group.LoadSide != GroupLoadSideEnum.Client)
                     .Where(group => !group.GroupName.Contains("TrainVisitor"))
                     .Where(group => !group.GroupName.Contains("DeployPuzzle_Repeat_Area"))
                     .Where(group => !group.GroupName.Contains("TrainVisiter")))

            if (oldGroupId.Contains(group.Id)) // check if it should be unloaded
            {
                if (group.ForceUnloadCondition.IsTrue(Scene.Player.MissionManager!.Data,
                        false) || // condition: Force Unload Condition
                    group.UnloadCondition.IsTrue(Scene.Player.MissionManager!.Data,
                        false)) // condition: Unload Condition  anyone of the conditions is true then unload
                {
                    foreach (var entity in Scene.Entities.Values.Where(entity => entity.GroupId == group.Id).ToList())
                    {
                        await Scene.RemoveEntity(entity, false);
                    }

                    Scene.Groups.Remove(group.Id);
                    unloadedGroups.Add(group.Id);
                }
                else if (group.OwnerMainMissionID != 0 &&
                         Scene.Player.MissionManager!.GetMainMissionStatus(group.OwnerMainMissionID) !=
                         MissionPhaseEnum.Accept) // condition: Owner Main Mission ID
                {
                    foreach (var entity in Scene.Entities.Values.Where(entity => entity.GroupId == group.Id).ToList())
                    {
                        await Scene.RemoveEntity(entity, false);
                    }

                    Scene.Groups.Remove(group.Id);
                    unloadedGroups.Add(group.Id);
                }
                else if (!group.SavedValueCondition.IsTrue(
                             Scene.Player.SceneData!
                                 .GetFloorSavedValues(Scene.FloorId))) // condition: Saved Value Condition
                {
                    foreach (var entity in Scene.Entities.Values.Where(entity => entity.GroupId == group.Id).ToList())
                    {
                        await Scene.RemoveEntity(entity, false);
                    }

                    Scene.Groups.Remove(group.Id);
                    unloadedGroups.Add(group.Id);
                }
                else if (ConfigManager.Config.ServerOption.EnableMission &&
                         group.RelatedBattleId.Count > 0 &&
                         !Scene.Player.MissionManager!.GetRunningSubMissionList()
                             .Any(x =>
                                 x.FinishType == MissionFinishTypeEnum.StageWin &&
                                 group.RelatedBattleId.Contains(x.ParamInt1)))
                {
                    foreach (var entity in Scene.Entities.Values.Where(entity => entity.GroupId == group.Id).ToList())
                    {
                        await Scene.RemoveEntity(entity, false);
                    }

                    Scene.Groups.Remove(group.Id);
                    unloadedGroups.Add(group.Id);
                }
            }
            else // check if it should be loaded
            {
                var groupList = await LoadGroup(group);
                if (groupList != null) loadedGroups.Add(group.Id);
            }

        if (loadedGroups.Count > 0 || unloadedGroups.Count > 0)
            await Scene.Player.SendPacket(PacketSceneGroupRefreshScNotify.ForGroupChanges(Scene, loadedGroups, unloadedGroups));
    }

    public virtual async ValueTask<List<BaseGameEntity>?> LoadGroup(GroupInfo info, bool forceLoad = false)
    {
        // When mission/quest systems are disabled, load conditions and saved-value gates are often never satisfied,
        // which can result in empty maps (no props/calyx/etc.). In that mode, treat all server-side groups as force-load.
        if (!ConfigManager.Config.ServerOption.EnableMission || !ConfigManager.Config.ServerOption.EnableQuest)
            forceLoad = true;

        if (!LoadGroups.Contains(info.Id)) return null; // check if group should be loaded in this dimension
        var missionData = Scene.Player.MissionManager!.Data; // get mission data
        if (info.LoadSide == GroupLoadSideEnum.Client) return null; // check if group should be loaded on client side
        if (info.GroupName.Contains("TrainVisitor")) return null; // certain group name
        if (info.GroupName.Contains("DeployPuzzle_Repeat_Area")) return null;
        if (info.GroupName.Contains("TrainVisiter")) return null;

        if (info.SystemUnlockCondition != null) // condition: System Unlock Condition
        {
            var result = info.SystemUnlockCondition.Operation != OperationEnum.Or; // operation
            foreach (var conditionId in info.SystemUnlockCondition.Conditions)
            {
                GameData.GroupSystemUnlockDataData.TryGetValue(conditionId, out var unlockExcel);
                if (unlockExcel == null) continue;
                var part = Scene.Player.QuestManager?.UnlockHandler.GetUnlockStatus(unlockExcel.UnlockID) ??
                           false; // check if unlock condition is met
                if (info.SystemUnlockCondition.Operation == OperationEnum.Or && part)
                {
                    result = true;
                    break;
                }

                if (info.SystemUnlockCondition.Operation == OperationEnum.And && !part)
                {
                    result = false;
                    break;
                }

                if (info.SystemUnlockCondition.Operation != OperationEnum.Not || !part) continue;
                result = false;
                break;
            }

            if (!result) return null;
        }

        if (ConfigManager.Config.ServerOption.EnableMission)
            if (!(info.OwnerMainMissionID == 0 || // condition: Owner Main Mission ID
                  Scene.Player.MissionManager!.GetMainMissionStatus(info.OwnerMainMissionID) ==
                  MissionPhaseEnum.Accept)) return null; // check if main mission is accepted

        if ((!info.LoadCondition.IsTrue(missionData) ||
             info.UnloadCondition.IsTrue(missionData,
                 false) || // condition: Load Condition, Unload Condition, Force Unload Condition
             info.ForceUnloadCondition.IsTrue(missionData, false)) &&
            !forceLoad) return null; // check if group should be loaded forcefully

        if (!info.SavedValueCondition.IsTrue(
                Scene.Player.SceneData!.FloorSavedData.GetValueOrDefault(Scene.FloorId, [])) &&
            !forceLoad) // condition: Saved Value Condition
            return null;

        if (Scene.Groups.Contains(info.Id) || Scene.Entities.Values.Any(x => x.GroupId == info.Id))
            return null;

        if (ConfigManager.Config.ServerOption.EnableMission &&
            info.RelatedBattleId.Count > 0 && !Scene.Player.MissionManager!.GetRunningSubMissionList().Any(x =>
                x.FinishType == MissionFinishTypeEnum.StageWin && info.RelatedBattleId.Contains(x.ParamInt1)))
            return null; // mission not activated

        // TODO atmosphere conditions

        // load
        Scene.Groups.Add(info.Id); // add group to loaded groups

        var entityList = new List<BaseGameEntity>();
        foreach (var npc in info.NPCList)
            try
            {
                if (await LoadNpc(npc, info) is { } entity) entityList.Add(entity);
            }
            catch
            {
                // ignored
            }

        foreach (var monster in info.MonsterList)
            try
            {
                if (await LoadMonster(monster, info) is { } entity) entityList.Add(entity);
            }
            catch
            {
                // ignored
            }

        foreach (var prop in info.PropList)
            try
            {
                if (await LoadProp(prop, info) is { } entity) entityList.Add(entity);
            }
            catch
            {
                // ignored
            }

        return entityList;
    }

    public virtual async ValueTask<List<BaseGameEntity>?> LoadGroup(int groupId, bool sendPacket = true)
    {
        var group = Scene.FloorInfo?.Groups.TryGetValue(groupId, out var v1) == true ? v1 : null;
        if (group == null) return null;
        var entities = await LoadGroup(group, true);

        if (sendPacket && entities != null)
            await Scene.Player.SendPacket(PacketSceneGroupRefreshScNotify.ForGroupChanges(Scene, [groupId]));

        return entities;
    }

    public virtual async ValueTask UnloadGroup(int groupId, bool sendPacket = true)
    {
        var group = Scene.FloorInfo?.Groups.TryGetValue(groupId, out var v1) == true ? v1 : null;
        if (group == null) return;

        var wasLoaded = Scene.Groups.Contains(groupId);

        foreach (var entity in Scene.Entities.Values.ToList())
            if (entity.GroupId == group.Id)
            {
                await Scene.RemoveEntity(entity, false);
                wasLoaded = true;
            }

        Scene.Groups.Remove(group.Id);

        if (wasLoaded && sendPacket)
            await Scene.Player.SendPacket(PacketSceneGroupRefreshScNotify.ForGroupChanges(Scene, [], [groupId]));
    }

    public virtual async ValueTask<EntityNpc?> LoadNpc(NpcInfo info, GroupInfo group, bool sendPacket = false)
    {
        if (info.IsClientOnly || info.IsDelete) return null;

        if (!GameData.NpcDataData.ContainsKey(info.NPCID)) return null;

        EntityNpc npc = new(Scene, group, info);
        await Scene.AddEntity(npc, sendPacket);

        return npc;
    }

    public virtual async ValueTask<EntityMonster?> LoadMonster(MonsterInfo info, GroupInfo group,
        bool sendPacket = false)
    {
        if (info.IsClientOnly || info.IsDelete) return null;

        GameData.NpcMonsterDataData.TryGetValue(info.NPCMonsterID, out var excel);
        if (excel == null) return null;

        EntityMonster entity = new(Scene, info.ToPositionProto(), info.ToRotationProto(), group.Id, info.ID, excel,
            info);
        await Scene.AddEntity(entity, sendPacket);
        return entity;
    }

    public virtual async ValueTask<EntityProp?> LoadProp(PropInfo info, GroupInfo group, bool sendPacket = false)
    {
        if (info.IsClientOnly || info.IsDelete) return null;

        // In "unlock all" mode (missions/quests disabled), allow loading props that aren't marked LoadOnInitial.
        if (!info.LoadOnInitial &&
            ConfigManager.Config.ServerOption.EnableMission &&
            ConfigManager.Config.ServerOption.EnableQuest)
            return null;

        GameData.MazePropData.TryGetValue(info.PropID, out var excel);
        if (excel == null) return null;

        var prop = new EntityProp(Scene, excel, group, info);

        if (excel.PropType == PropTypeEnum.PROP_SPRING)
        {
            Scene.HealingSprings.Add(prop);
            await prop.SetState(PropStateEnum.CheckPointEnable);
        }

        // load from database
        var propData = Scene.Player.GetScenePropData(Scene.FloorId, group.Id, info.ID);
        if (propData != null && Scene.Excel.PlaneType != PlaneTypeEnum.Raid) // raid is not saved
        {
            prop.State = propData.State;
        }
        else
        {
            if (Scene.Excel.PlaneType == PlaneTypeEnum.Raid)
                prop.State = info.State;
            else
                // elevator
                prop.State = prop.Excel.PropType == PropTypeEnum.PROP_ELEVATOR ? PropStateEnum.Elevator1 : info.State;
        }

        var timelineData = Scene.Player.GetScenePropTimelineData(Scene.FloorId, group.Id, info.ID);
        prop.PropTimelineData = timelineData;

        if (group.GroupName.Contains("Machine"))
        {
            await prop.SetState(PropStateEnum.Open);
            await Scene.AddEntity(prop, sendPacket);
            return prop;
        }

        if (prop.PropInfo.Name.Contains("Case") && prop.PropInfo.State == PropStateEnum.Open)
            await prop.SetState(PropStateEnum.Closed);

        if (prop.PropInfo.PropID == 1003)
        {
            if (prop.PropInfo.MappingInfoID != 2220) return prop;
            await prop.SetState(PropStateEnum.Open);
        }

        if (prop.PropInfo.PropID is 104006 or 104005) await prop.SetState(PropStateEnum.Open);

        await Scene.AddEntity(prop, sendPacket);

        return prop;
    }
}
