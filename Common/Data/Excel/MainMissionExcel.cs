using HyacineCore.Server.Database.Player;
using HyacineCore.Server.Database.Quests;
using HyacineCore.Server.Enums;
using HyacineCore.Server.Enums.Mission;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using MissionInfo = HyacineCore.Server.Data.Config.MissionInfo;

namespace HyacineCore.Server.Data.Excel;

[ResourceEntity("MainMission.json")]
public class MainMissionExcel : ExcelResource
{
    public int MainMissionID { get; set; }
    public HashName Name { get; set; } = new();

    [JsonConverter(typeof(StringEnumConverter))]
    public OperationEnum TakeOperation { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public OperationEnum BeginOperation { get; set; }

    public List<MissionParam> TakeParam { get; set; } = [];
    public List<MissionParam> BeginParam { get; set; } = [];
    public int RewardID { get; set; }
    public List<int> SubRewardList { get; set; } = [];

    // The mission the client tracks after this one (1031001 -> 1031101).
    public int NextTrackMainMission { get; set; }

    [JsonIgnore] public MissionInfo MissionInfo { get; protected set; } = new();
    [JsonIgnore] public List<int> SubMissionIds { get; set; } = [];

    public override int GetId()
    {
        return MainMissionID;
    }

    public override void Loaded()
    {
        GameData.MainMissionData[GetId()] = this;
    }

    public void SetMissionInfo(MissionInfo missionInfo)
    {
        MissionInfo = missionInfo;
        if (missionInfo != null)
            foreach (var sub in missionInfo.SubMissionList)
            {
                SubMissionIds.Add(sub.ID);
                GameData.SubMissionInfoData[sub.ID] = new SubMissionData(sub.ID)
                {
                    MainMissionId = MainMissionID,
                    MainMissionInfo = MissionInfo,
                    SubMissionInfo = sub
                };
            }
    }

    public bool IsEqual(MissionData data)
    {
        var result = TakeOperation == OperationEnum.And;
        foreach (var param in TakeParam)
            if (param.IsEqual(data))
            {
                if (TakeOperation != OperationEnum.And) return true;
            }
            else
            {
                if (TakeOperation == OperationEnum.And) return false;
            }

        return result;
    }
}

public class MissionParam
{
    [JsonConverter(typeof(StringEnumConverter))]
    public MissionTakeTypeEnum Type { get; set; }

    public int Value { get; set; }

    public bool IsEqual(MissionData data)
    {
        switch (Type)
        {
            case MissionTakeTypeEnum.Auto:
                return true;
            case MissionTakeTypeEnum.PlayerLevel:
                return PlayerData.GetPlayerByUid(data.Uid)!.Level >= Value;
            case MissionTakeTypeEnum.WorldLevel:
                return PlayerData.GetPlayerByUid(data.Uid)!.WorldLevel >= Value;
            case MissionTakeTypeEnum.MultiSequence:
                var value = data.GetMainMissionStatus(Value);
                return value == MissionPhaseEnum.Finish;
            default:
                return false;
        }
    }
}