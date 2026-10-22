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

        await scene.UpdateGroupState((int)info.GroupId, (int)info.GroupState);
        await connection.SendPacket(new PacketGroupStateChangeScRsp(Retcode.RetSucc, info));
    }
}
