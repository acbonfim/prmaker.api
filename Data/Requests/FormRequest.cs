using solvace.prform.Data.Entities;

namespace solvace.prform.Data.Requests;

public class FormRequest
{
    public string Description { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;

    public Form CreateForm(FormRequest request)
    {
        return new Form(request.Description, request.EnvironmentName);
    }
}