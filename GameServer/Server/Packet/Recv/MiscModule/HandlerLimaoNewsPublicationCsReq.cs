using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.MiscModule;

[Opcode(CmdIds.FEPKFPBPPLA)]
public class HandlerLimaoNewsPublicationCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = FEPKFPBPPLA.Parser.ParseFrom(data);
        // The news popup waits behind a fullscreen block until the requested publication is echoed.
        var reply = new BasePacket(CmdIds.CELNLKDAMKA);
        reply.SetData(new CELNLKDAMKA { FOPKKPHCIIJ = req.FOPKKPHCIIJ });
        await connection.SendPacket(reply);
    }
}
