using HyacineCore.Server.Enums.Scene;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.Scene;

public class PacketGroupStateChangeScNotify : BasePacket
{
    public PacketGroupStateChangeScNotify(int entryId, int groupId, PropStateEnum propState) : this(entryId,
        groupId, (int)propState)
    {
    }

    // A change the client is to make carries the new state in field 15 as well, like the reply to its own request
    // (HandlerGroupStateChangeCsReq): with GroupState alone the 4.1 client keeps the group where it was (1036002's
    // D4 gashapon, G587, stayed in 0 and never played its gacha).
    public PacketGroupStateChangeScNotify(int entryId, int groupId, int state, bool change = false) : base(
        CmdIds.GroupStateChangeScNotify)
    {
        var notify = new GroupStateChangeScNotify
        {
            GroupStateInfo = new GroupStateInfo
            {
                EntryId = (uint)entryId,
                GroupId = (uint)groupId,
                GroupState = (uint)state,
                LAPJHJBLNEP = change ? (uint)state : 0
            }
        };

        SetData(notify);
    }
}