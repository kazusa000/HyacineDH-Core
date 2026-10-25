using HyacineCore.Server.GameServer.Game.Mission;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.GameServer.Game.Scene;
using HyacineCore.Server.GameServer.Game.Scene.Entity;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.Scene;

public class PacketSceneGroupRefreshScNotify : BasePacket
{
    public PacketSceneGroupRefreshScNotify(PlayerInstance player, List<BaseGameEntity>? addEntity = null,
        List<BaseGameEntity>? removeEntity = null,
        SceneGroupRefreshType refreshType = SceneGroupRefreshType.Afibfmafncc)
        : base(CmdIds.SceneGroupRefreshScNotify)
    {
        var proto = new SceneGroupRefreshScNotify
        {
            FloorId = (uint)player.Data.FloorId,
            DimensionId = (uint)((player.SceneInstance!.EntityLoader as StoryLineEntityLoader)?.DimensionId ?? 0)
        };
        Dictionary<int, GroupRefreshInfo> refreshInfo = [];

        foreach (var e in removeEntity ?? [])
        {
            var group = new GroupRefreshInfo
            {
                GroupId = (uint)e.GroupId,
                RefreshType = refreshType == SceneGroupRefreshType.Loaded
                    ? SceneGroupRefreshType.Afibfmafncc : refreshType
            };
            group.RefreshEntity.Add(new SceneEntityRefreshInfo
            {
                DeleteEntity = (uint)e.EntityId
            });

            if (refreshInfo.TryGetValue(e.GroupId, out var value))
                value.RefreshEntity.AddRange(group.RefreshEntity);
            else
                refreshInfo[e.GroupId] = group;
        }

        foreach (var e in addEntity ?? [])
        {
            var group = new GroupRefreshInfo
            {
                GroupId = (uint)e.GroupId,
                RefreshType = refreshType
            };
            group.RefreshEntity.Add(new SceneEntityRefreshInfo
            {
                AddEntity = e.ToProto()
            });

            if (refreshInfo.TryGetValue(e.GroupId, out var value))
                value.RefreshEntity.AddRange(group.RefreshEntity);
            else
                refreshInfo[e.GroupId] = group;
        }

        foreach (var group in refreshInfo.Values)
        {
            // Loaded reads Unk1.EntityList in the 4.1 client. RefreshEntity is consumed only by the
            // incremental path. Group zero is the separate avatar path, whose original envelope is retained.
            if (group.RefreshType == SceneGroupRefreshType.Loaded && group.GroupId != 0)
            {
                group.Unk1 = player.SceneInstance!.ToGroupProto((int)group.GroupId);
                group.State = group.Unk1.State;
                group.RefreshEntity.Clear();
            }
            proto.GroupRefreshList.Add(group);
        }

        SetData(proto);
    }

    public PacketSceneGroupRefreshScNotify(PlayerInstance player, BaseGameEntity? addEntity = null,
        BaseGameEntity? removeEntity = null, SceneGroupRefreshType refreshType = SceneGroupRefreshType.Afibfmafncc) :
        this(player, addEntity == null ? [] : [addEntity], removeEntity == null ? [] : [removeEntity], refreshType)
    {
    }

    private PacketSceneGroupRefreshScNotify(SceneGroupRefreshScNotify proto) : base(CmdIds.SceneGroupRefreshScNotify)
    {
        SetData(proto);
    }

    public static PacketSceneGroupRefreshScNotify ForGroupChanges(SceneInstance scene,
        IEnumerable<int> loadedGroupIds, IEnumerable<int>? unloadedGroupIds = null)
    {
        var proto = new SceneGroupRefreshScNotify
        {
            FloorId = (uint)scene.FloorId,
            DimensionId = (uint)((scene.EntityLoader as StoryLineEntityLoader)?.DimensionId ?? 0)
        };
        foreach (var id in (unloadedGroupIds ?? []).Distinct())
            proto.GroupRefreshList.Add(new GroupRefreshInfo
            {
                GroupId = (uint)id,
                RefreshType = SceneGroupRefreshType.Unload
            });
        foreach (var id in loadedGroupIds.Distinct())
        {
            var group = scene.ToGroupProto(id);
            proto.GroupRefreshList.Add(new GroupRefreshInfo
            {
                GroupId = (uint)id,
                State = group.State,
                RefreshType = SceneGroupRefreshType.Loaded,
                Unk1 = group
            });
        }
        return new PacketSceneGroupRefreshScNotify(proto);
    }

    public PacketSceneGroupRefreshScNotify(SceneInstance scene, List<GroupPropertyRefreshData> refreshDataList) : base(
        CmdIds.SceneGroupRefreshScNotify)
    {
        var proto = new SceneGroupRefreshScNotify
        {
            FloorId = (uint)scene.FloorId,
            DimensionId = (uint)((scene.EntityLoader as StoryLineEntityLoader)?.DimensionId ?? 0)
        };

        Dictionary<int, List<GroupPropertyRefreshData>> refreshDataDict = [];
        foreach (var data in refreshDataList)
        {
            if (!refreshDataDict.TryGetValue(data.GroupId, out var list))
            {
                list = [];
                refreshDataDict[data.GroupId] = list;
            }

            list.Add(data);
        }

        foreach (var (groupId, dataList) in refreshDataDict)
        {
            var group = new GroupRefreshInfo
            {
                GroupId = (uint)groupId,
                RefreshType = SceneGroupRefreshType.Afibfmafncc
            };

            foreach (var data in dataList)
                group.IJABHDPLDMI.Add(new MAALENOAMPC
                {
                    JDJOCEHPOKF = data.NewValue,
                    EAGKBEMCFAB = data.OldValue,
                    NOCBONMOOGC = data.PropertyName
                });

            proto.GroupRefreshList.Add(group);
        }

        SetData(proto);
    }
}

public record GroupPropertyRefreshData(int GroupId, string PropertyName, int OldValue, int NewValue);
