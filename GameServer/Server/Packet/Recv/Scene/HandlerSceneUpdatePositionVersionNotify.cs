using HyacineCore.Server.GameServer.Server.Packet.Send.Scene;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.Scene;

// The client waits for the server to echo its position version before it syncs movement.
[Opcode(CmdIds.SceneUpdatePositionVersionNotify)]
public class HandlerSceneUpdatePositionVersionNotify : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = SceneUpdatePositionVersionNotify.Parser.ParseFrom(data);
        await connection.SendPacket(new PacketSceneUpdatePositionVersionNotify(req.PosVersion));
    }
}
