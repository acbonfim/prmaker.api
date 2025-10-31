using solvace.prform.domain.Entities;

namespace solvace.prform.domain.Requests;

public class PullRequestRegisterRequest
{
    public string Description { get; set; } = string.Empty;
    public string CardNumber { get; set; } = string.Empty;
    public string RootCause { get; set; } = string.Empty;
    public int FormId { get; set; }
    public Guid UserId { get; set; }

    public PullRequestRegister Create(PullRequestRegisterRequest request)
    {
        return new PullRequestRegister(request);
    }
}