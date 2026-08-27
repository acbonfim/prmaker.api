namespace solvace.github.domain.ValueObjects;

public class HttpJsonResponse
{
    public int StatusCode { get; set; }
    public string Content { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/json";
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;

    public static HttpJsonResponse Ok(string content, string contentType = "application/json") => new()
    {
        StatusCode = 200,
        Content = content,
        ContentType = contentType
    };

    public static HttpJsonResponse From(int statusCode, string content, string contentType = "application/json") => new()
    {
        StatusCode = statusCode,
        Content = content,
        ContentType = contentType
    };
}


