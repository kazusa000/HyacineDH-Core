using HyacineCore.Server.Internationalization;

using HyacineCore.Server.Data;
using HyacineCore.Server.Util;

namespace HyacineCore.Server.Command.Command.Cmd;

[CommandInfo("relic", "Game.Command.Relic.Desc", "Game.Command.Relic.Usage")]
public class CommandRelic : ICommand
{
    [CommandDefault]
    public async ValueTask GiveRelic(CommandArg arg)
    {
        var player = arg.Target?.Player;
        if (player == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return;
        }

        if (arg.BasicArgs.Count < 2)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
            return;
        }

        // Parse character
        arg.CharacterArgs.TryGetValue("x", out var str);
        arg.CharacterArgs.TryGetValue("l", out var levelStr);
        str ??= "1";
        levelStr ??= "0";
        if (!int.TryParse(str, out var amount) || !int.TryParse(levelStr, out var level) || amount <= 0 ||
            !int.TryParse(arg.BasicArgs[0], out var relicId) ||
            !GameData.RelicConfigData.TryGetValue(relicId, out var relicConfig) || level < 0 || level > relicConfig.MaxLevel)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
            return;
        }

        // PutItem rejects a full bag without propagating an error to HandleRelic. Check before
        // the batch and confirm insertion below so MUIP never receives a false success.
        if (amount > GameConstants.INVENTORY_MAX_RELIC - player.InventoryManager!.Data.RelicItems.Count)
        {
            await arg.SendMsg("错误：遗器背包空间不足，本次未发放。请先清理背包或减少数量。");
            return;
        }

        // Parse main affix
        var startIndex = 1;
        var mainAffixId = 0;
        if (!arg.BasicArgs[1].Contains(':'))
        {
            if (!int.TryParse(arg.BasicArgs[1], out mainAffixId))
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
                return;
            }
            startIndex++;
        }

        // Parse sub affixes
        var subAffixes = new List<(int, int)>();
        for (var ii = startIndex; ii < arg.BasicArgs.Count; ii++)
        {
            var subAffix = arg.BasicArgs[ii].Split(':');
            if (subAffix.Length != 2 || !int.TryParse(subAffix[0], out var subId) ||
                !int.TryParse(subAffix[1], out var subLevel))
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
                return;
            }

            subAffixes.Add((subId, subLevel));
        }

        for (var i = 0; i < amount; i++)
        {
            var (ret, item) = await player.InventoryManager!.HandleRelic(
                relicId, ++player.InventoryManager!.Data.NextUniqueId,
                level, mainAffixId, subAffixes);

            switch (ret)
            {
                case 1:
                    await arg.SendMsg(I18NManager.Translate("Game.Command.Relic.RelicNotFound"));
                    return;
                case 2:
                    await arg.SendMsg(I18NManager.Translate("Game.Command.Relic.InvalidMainAffixId"));
                    return;
                case 3:
                    await arg.SendMsg(I18NManager.Translate("Game.Command.Relic.InvalidSubAffixId"));
                    return;
            }
            if (item == null || !player.InventoryManager.Data.RelicItems.Any(x => x.UniqueId == item.UniqueId))
            {
                await arg.SendMsg($"错误：遗器未能入库，本次已发放 {i}/{amount} 件。请核对背包后重试剩余数量。");
                return;
            }
        }

        await arg.SendMsg(I18NManager.Translate("Game.Command.Relic.RelicGiven", player.Uid.ToString(),
            amount.ToString(), arg.BasicArgs[0], mainAffixId.ToString()));
    }
}
