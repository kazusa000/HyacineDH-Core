using System;
using HyacineCore.Server.Proto;
using SqlSugar;

namespace HyacineCore.Server.Database.Activity;

[SugarTable("Activity")]
public class ActivityData : BaseDatabaseDataHelper
{
    [SugarColumn(IsJson = true)] public TrialActivityData TrialActivityData { get; set; } = new();
    [SugarColumn(IsJson = true)] public LoginActivityData LoginActivityData { get; set; } = new();

    // Movie racing (the cinema hamster-ball minigame) best results by racing type, then level.
    // Nullable for databases created before these were kept.
    [SugarColumn(IsNullable = true, IsJson = true, ColumnDataType = "TEXT")]
    public Dictionary<int, Dictionary<int, MovieRacingResult>> MovieRacingData { get; set; } = [];
}

public class MovieRacingResult
{
    public uint Score { get; set; }
    public uint Extra { get; set; } // PFLMNMAGFKP, kept as the client sends it

    public FGCDAFADHIM ToProto(int type, int level)
    {
        return new FGCDAFADHIM
        {
            Type = (MovieRacingType)type,
            Level = (uint)level,
            HPKMEMFPLOH = Score,
            PFLMNMAGFKP = Extra
        };
    }
}

public class LoginActivityData
{
    public Dictionary<uint, List<uint>> TakenRewards { get; set; } = new();
    public Dictionary<uint, uint> LoginDays { get; set; } = new();
    public long LastUpdateTick { get; set; }

    public List<HyacineCore.Server.Proto.LoginActivityData> ToProto(
        Func<uint, uint>? panelIdResolver = null,
        uint fallbackPanelId = 10130)
    {
        var protoList = new List<HyacineCore.Server.Proto.LoginActivityData>();

        foreach (var kv in LoginDays)
        {
            var protoData = new HyacineCore.Server.Proto.LoginActivityData
            {
                Id = kv.Key,
                LoginDays = kv.Value,
                PanelId = panelIdResolver?.Invoke(kv.Key) ?? fallbackPanelId
            };

            if (TakenRewards.TryGetValue(kv.Key, out var takenList))
            {
                protoData.OMCIOCFBIFA.AddRange(takenList);
            }

            protoList.Add(protoData);
        }

        return protoList;
    }
}

public class TrialActivityData
{
    public List<TrialActivityResultData> Activities { get; set; } = new();
    public int CurTrialStageId { get; set; }

    public List<TrialActivityInfo> ToProto()
    {
        var proto = new List<TrialActivityInfo>();

        foreach (var activity in Activities)
        {
            proto.Add(new TrialActivityInfo
            {
                StageId = (uint)activity.StageId,
                TakenReward = activity.TakenReward
            });
        }

        return proto;
    }
}

public class TrialActivityResultData
{
    public int StageId { get; set; }
    public bool TakenReward { get; set; }
}
