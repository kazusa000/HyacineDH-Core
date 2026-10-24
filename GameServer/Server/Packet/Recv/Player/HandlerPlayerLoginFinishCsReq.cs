using HyacineCore.Server.GameServer.Server.Packet.Send.Mission;
using HyacineCore.Server.Kcp;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.Player;

[Opcode(CmdIds.PlayerLoginFinishCsReq)]
public class HandlerPlayerLoginFinishCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        await connection.SendPacket(CmdIds.PlayerLoginFinishScRsp);
        if (connection.Player?.MissionManager != null)
            await connection.SendPacket(
                new PacketFinishedMissionScNotify(connection.Player.MissionManager.GetAllFinishedMissionIds()));
        //var list = connection.Player!.MissionManager!.GetRunningSubMissionIdList();
        //await connection.SendPacket(new PacketMonthCardRewardNotify([new ItemData
        //{
        //    ItemId = 1,
        //    Count = 90
        //}]));
    }
}