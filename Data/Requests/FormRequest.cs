using solvace.prform.Data.Entities;

namespace solvace.prform.Data.Requests;

public class FormRequest
{
    public string Description { get; set; }
    public string EnvironmentName { get; set; }

    public Form CreateForm(FormRequest request)
    {
        return new Form(request.Description, request.EnvironmentName);
    }
}