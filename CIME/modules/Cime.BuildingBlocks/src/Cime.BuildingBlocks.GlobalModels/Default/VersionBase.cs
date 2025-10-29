

namespace Cliqx.BuildingBlocks.GlobalModels;

public class VersionBase
{
    public string VersionAPI { get; set; }
    public DateOnly DateRelease { get; set; }
    public string Release { get; set; }
    public string TimeZone { get; set; }
}
