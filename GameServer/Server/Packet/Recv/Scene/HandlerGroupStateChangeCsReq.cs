using HyacineCore.Server.GameServer.Game.Mission;
using HyacineCore.Server.GameServer.Server.Packet.Send.Scene;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.Scene;

[Opcode(CmdIds.GroupStateChangeCsReq)]
public class HandlerGroupStateChangeCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = GroupStateChangeCsReq.Parser.ParseFrom(data);
        var info = req.GroupStateInfo;
        var scene = connection.Player!.SceneInstance;

        if (info == null || scene == null || !scene.Groups.Contains((int)info.GroupId))
        {
            await connection.SendPacket(new PacketGroupStateChangeScRsp(Retcode.RetGroupNotExist, info));
            return;
        }

        // The client sends its current state in GroupState and the state it asks for in field 15
        // (103030304: Hanu TV group 156 sends 1 -> 2); older requests carry only GroupState.
        var target = info.LAPJHJBLNEP != 0 ? info.LAPJHJBLNEP : info.GroupState;
        await scene.UpdateGroupState((int)info.GroupId, (int)target);
        await PuzzleFallback.OnGroupInteract(connection.Player!, (int)info.GroupId);
        await connection.SendPacket(new PacketGroupStateChangeScRsp(Retcode.RetSucc, info));
    }
}
