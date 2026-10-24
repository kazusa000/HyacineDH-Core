using HyacineCore.Server.GameServer.Server.Packet.Send.MapRotation;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.MapRotation;

[Opcode(CmdIds.InteractChargerCsReq)]
public class HandlerInteractChargerCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = InteractChargerCsReq.Parser.ParseFrom(data);

        await new HyacineCore.Server.GameServer.Game.Scene.MapRotationManager(connection.Player!).Charge(req);
    }
}
