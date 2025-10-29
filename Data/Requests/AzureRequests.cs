namespace solvace.prform.Data.Requests;

public class UpdateHoursRequest
{
    public decimal Hours { get; set; }
    public string TypeHours { get; set; } = string.Empty;
}

public class UpdateStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

public class UpdateRootCauseRequest
{
    public string RootCause { get; set; } = string.Empty;
}

public class AttachFileRequest
{
    public string FileName { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
}
