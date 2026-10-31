namespace HyacineCore.Server.Data.Excel;

[ResourceEntity("TutorialData.json")]
public class TutorialDataExcel : ExcelResource
{
    public int TutorialID { get; set; }
    public int Priority { get; set; }

    // Any one of these ends the tutorial; the client reports it when it sees the event happen.
    public List<TutorialTriggerParam> FinishTriggerParams { get; set; } = [];

    public override int GetId()
    {
        return TutorialID;
    }

    public override void Loaded()
    {
        GameData.TutorialDataData.TryAdd(TutorialID, this);
    }
}

public class TutorialTriggerParam
{
    public string TriggerType { get; set; } = "";
    public string TriggerParam { get; set; } = "";
}