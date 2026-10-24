using HyacineCore.Server.GameServer.Server.Packet.Send.MapRotation;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.MapRotation;

[Opcode(CmdIds.DeployRotatorCsReq)]
public class HandlerDeployRotaterCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = DeployRotatorCsReq.Parser.ParseFrom(data);

        await new HyacineCore.Server.GameServer.Game.Scene.MapRotationManager(connection.Player!).Deploy(req);
    }
}
