using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.TalkEvent;

// Every NPC counts as already met (as March7thHoney 4.2); the empty reply gave the client no status at all.
public class PacketGetFirstTalkNpcScRsp : BasePacket
{
    public PacketGetFirstTalkNpcScRsp(GetFirstTalkNpcCsReq req) : base(CmdIds.GetFirstTalkNpcScRsp)
    {
        var rsp = new GetFirstTalkNpcScRsp();
        foreach (var id in req.NpcIdList)
            rsp.NpcMeetStatusList.Add(new FirstNpcTalkInfo { NpcId = id, IsMeet = true });
        SetData(rsp);
    }
}
