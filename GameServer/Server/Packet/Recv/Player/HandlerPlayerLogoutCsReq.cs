using HyacineCore.Server.Database;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Util;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.Player;

[Opcode(CmdIds.PlayerLogoutCsReq)]
public class HandlerPlayerLogoutCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        await connection.SendPacket(CmdIds.None);
        connection.Stop();
        // Save now instead of waiting for the periodic save: the server may be stopped right after.
        if (connection.Player != null)
        {
            DatabaseHelper.ToSaveUidList.SafeAdd(connection.Player.Uid);
            DatabaseHelper.Instance?.SaveDatabase();
        }
    }
}
