using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Server.Packet.Send.Mission;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.Mission;

[Opcode(CmdIds.FinishCosumeItemMissionCsReq)]
public class HandlerFinishCosumeItemMissionCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = FinishCosumeItemMissionCsReq.Parser.ParseFrom(data);

        var player = connection.Player!;
        var mission = player.MissionManager?.GetSubMissionInfo((int)req.SubMissionId);
        if (mission == null)
        {
            await connection.SendPacket(new PacketFinishCosumeItemMissionScRsp());
            return;
        }

        if (mission.FinishType == MissionFinishTypeEnum.UseSelectedItem)
        {
            // The player hands over one of the listed items; the request names the one picked.
            var allowed = mission.ParamItemList?.Select(x => x.ItemID).ToHashSet() ?? [];
            foreach (var item in req.ItemList?.ItemList_ ?? [])
                if (allowed.Contains((int)item.ItemId))
                    await player.InventoryManager!.RemoveItem((int)item.ItemId, (int)Math.Max(item.Num, 1));
        }
        else
        {
            foreach (var param in mission.ParamItemList ?? [])
                await player.InventoryManager!.RemoveItem(param.ItemID, param.ItemNum);
        }

        await player.MissionManager!.FinishSubMission((int)req.SubMissionId);

        await connection.SendPacket(new PacketFinishCosumeItemMissionScRsp(req.SubMissionId));
    }
}