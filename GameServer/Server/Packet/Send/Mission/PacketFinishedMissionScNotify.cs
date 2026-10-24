using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.Mission;

// Finished main and sub mission IDs: the whole list after login, then each one as it finishes (as March7thHoney 4.2).
public class PacketFinishedMissionScNotify : BasePacket
{
    public PacketFinishedMissionScNotify(IEnumerable<int> missionIds) : base(CmdIds.FinishedMissionScNotify)
    {
        var proto = new FinishedMissionScNotify();
        proto.FinishedMissionId.AddRange(missionIds.Where(x => x > 0).Select(x => (uint)x).Distinct());
        SetData(proto);
    }
}
