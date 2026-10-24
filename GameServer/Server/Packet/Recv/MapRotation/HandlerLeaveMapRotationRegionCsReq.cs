using HyacineCore.Server.Kcp;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.MapRotation;

[Opcode(CmdIds.LeaveMapRotationRegionCsReq)]
public class HandlerLeaveMapRotationRegionCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = HyacineCore.Server.Proto.LeaveMapRotationRegionCsReq.Parser.ParseFrom(data);
        await new HyacineCore.Server.GameServer.Game.Scene.MapRotationManager(connection.Player!).Leave(req);
    }
}
