using HyacineCore.Server.GameServer.Server.Packet.Send.Mission;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.Mission;

[Opcode(CmdIds.FinishTalkMissionCsReq)]
public class HandlerFinishTalkMissionCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = FinishTalkMissionCsReq.Parser.ParseFrom(data);
        var player = connection.Player!;

        var manager = player.MissionManager!;
        var status = req.CustomValueList.Count == 0 ? Retcode.RetSucc :
            manager.RecordTalkCustomValues((int)req.SubMissionId, req.CustomValueList);
        if (status == Retcode.RetSucc)
        {
            await manager.HandleTalkStr(req.TalkStr);

            if (req.CustomValueList.Count > 0)
                await manager.HandleCustomValue([.. req.CustomValueList], (int)req.SubMissionId);
        }

        var packet = new BasePacket(CmdIds.FinishTalkMissionScRsp);
        packet.SetData(new FinishTalkMissionScRsp
        {
            Retcode = (uint)status, TalkStr = req.TalkStr, SubMissionId = req.SubMissionId,
            CustomValueList = { req.CustomValueList }
        });
        await connection.SendPacket(packet);
    }
}
