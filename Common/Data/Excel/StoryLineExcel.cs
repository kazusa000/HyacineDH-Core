using HyacineCore.Server.Enums.Mission;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace HyacineCore.Server.Data.Excel;

[ResourceEntity("StoryLine.json")]
public class StoryLineExcel : ExcelResource
{
    public int StoryLineID { get; set; }
    public StoryLineCondition BeginCondition { get; set; } = new();
    public StoryLineCondition EndCondition { get; set; } = new();
    public int InitEntranceID { get; set; }
    public int InitGroupID { get; set; }
    public int InitAnchorID { get; set; }

    // "[BetweenSubMission:a,b]": the line shows from taking sub-mission a until finishing b.
    public string ShowCondition { get; set; } = "";

    public (int From, int To)? ShowRange()
    {
        var match = System.Text.RegularExpressions.Regex.Match(ShowCondition, @"^\[BetweenSubMission:(\d+),(\d+)\]$");
        if (!match.Success) return null;
        return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
    }

    public override int GetId()
    {
        return StoryLineID;
    }

    public override void Loaded()
    {
        GameData.StoryLineData[StoryLineID] = this;
    }
}

public class StoryLineCondition
{
    [JsonConverter(typeof(StringEnumConverter))]
    public StoryLineConditionTypeEnum Type { get; set; } = new();

    public int Param { get; set; }
}