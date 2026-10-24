using HyacineCore.Server.GameServer.Game.Scene;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.MapRotation;

[Opcode(CmdIds.GetMapRotationDataCsReq)]
public class HandlerGetMapRotationDataCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var manager = new MapRotationManager(connection.Player!);
        await manager.Send(CmdIds.GetMapRotationDataScRsp, manager.Read());
    }
}
