using HyacineCore.Server.Data;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.StoryLine;

public class PacketStoryLineInfoScNotify : BasePacket
{
    public PacketStoryLineInfoScNotify(PlayerInstance player) : base(CmdIds.StoryLineInfoScNotify)
    {
        var storyLineIdList =
            player.StoryLineManager?.StoryLineData.RunningStoryLines.Keys.Select(x => (uint)x).ToList() ?? [];
        storyLineIdList.Insert(0, 0);

        // The two lists' names are swapped in this proto: the 4.1 client reads field 11 (UnfinishedStoryLineIdList
        // here) as the line's trial avatars and field 4 (TrialAvatarIdList) as the lines. With the lines in field 11
        // it creates trial avatar 0 when switching to a line (AddTrialPlayer fails in CheckTrialAvatarAvailable) and
        // never enters it: after a login inside 1031101 it tracked a side mission and skipped the line's groups.
        var proto = new StoryLineInfoScNotify
        {
            CurStoryLineId = (uint)(player.StoryLineManager?.StoryLineData.CurStoryLineId ?? 0),
            TrialAvatarIdList = { storyLineIdList }
        };

        if (!proto.TrialAvatarIdList.Contains(proto.CurStoryLineId))
            proto.TrialAvatarIdList.Add(proto.CurStoryLineId);

        GameData.StroyLineTrialAvatarDataData.TryGetValue(player.StoryLineManager!.StoryLineData.CurStoryLineId,
            out var storyExcel);
        if (storyExcel != null)
            proto.UnfinishedStoryLineIdList.AddRange(storyExcel.InitTrialAvatarList.Select(x => (uint)x));

        SetData(proto);
    }
}