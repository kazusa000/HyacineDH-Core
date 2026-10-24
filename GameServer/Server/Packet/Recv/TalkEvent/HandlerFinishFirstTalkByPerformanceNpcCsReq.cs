using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.TalkEvent;

// Without a handler the client got an empty reply without the performance ID (March7thHoney 4.2 echoes it).
[Opcode(CmdIds.FinishFirstTalkByPerformanceNpcCsReq)]
public class HandlerFinishFirstTalkByPerformanceNpcCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = FinishFirstTalkByPerformanceNpcCsReq.Parser.ParseFrom(data);
        var packet = new BasePacket(CmdIds.FinishFirstTalkByPerformanceNpcScRsp);
        packet.SetData(new FinishFirstTalkByPerformanceNpcScRsp
            { PerformanceId = req.PerformanceId, Reward = new ItemList() });
        await connection.SendPacket(packet);
    }
}
