using HyacineCore.Server.Data.Config.Scene;
using HyacineCore.Server.Data.Excel;
using HyacineCore.Server.Database.Scene;
using HyacineCore.Server.Enums.Scene;
using HyacineCore.Server.GameServer.Server.Packet.Send.Scene;
using HyacineCore.Server.Proto;
using HyacineCore.Server.Util;

namespace HyacineCore.Server.GameServer.Game.Scene.Entity;

public class EntityProp(SceneInstance scene, MazePropExcel excel, GroupInfo group, PropInfo prop) : BaseGameEntity
{
    public Position Position { get; set; } = prop.ToPositionProto();
    public Position Rotation { get; set; } = prop.ToRotationProto();
    public SceneInstance Scene { get; set; } = scene;
    public PropStateEnum State { get; set; } = PropStateEnum.Closed;
    public int InstId { get; set; } = prop.ID;
    public MazePropExcel Excel { get; set; } = excel;
    public PropInfo PropInfo { get; set; } = prop;
    public GroupInfo Group { get; set; } = group;
    public ScenePropTimelineData? PropTimelineData { get; set; }
    public override int EntityId { get; set; }
    public override int GroupId { get; set; } = group.Id;

    public override SceneEntityInfo ToProto()
    {
        var prop = new ScenePropInfo
        {
            PropId = (uint)Excel.ID,
            PropState = (uint)State
        };

        if (PropTimelineData != null)
            prop.ExtraInfo = new PropExtraInfo
            {
                TimelineInfo = PropTimelineData.ToProto()
            };

        return new SceneEntityInfo
        {
            EntityId = (uint)EntityId,
            GroupId = (uint)GroupId,
            Motion = new MotionInfo
            {
                Pos = Position.ToProto(),
                Rot = Rotation.ToProto()
            },
            InstId = (uint)InstId,
            Prop = prop
        };
    }

    public async ValueTask SetState(PropStateEnum state)
    {
        if (state == State) return;
        await SetState(state, Scene.IsLoaded);
    }

    public async ValueTask SetState(PropStateEnum state, bool sendPacket)
    {
        //if (State == PropStateEnum.Open) return;  // already open   DO NOT CLOSE AGAIN
        State = state;
        if (sendPacket)
        {
            // Re-sending the entity with refresh type 3 updates its state in place (as March7thHoney 4.2 does).
            // Deleting and re-adding it respawned the prop, so client state listeners never ran: a console
            // switched to Open without playing its animation or opening the linked door (Chap02 SimpleDoor).
            await Scene.Player.SendPacket(new PacketSceneGroupRefreshScNotify(Scene.Player, this, null,
                SceneGroupRefreshType.Afibfmafncc));
        }

        // save
        if (Group.SaveType == SaveTypeEnum.Reset) return;
        Scene.Player.SetScenePropData(Scene.FloorId, Group.Id, PropInfo.ID, state);
    }
}
