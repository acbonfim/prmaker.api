using Newtonsoft.Json;

namespace Cime.BuildingBlocks.Security
{
    public static class ResponseExtensions
    {
        public static T? AsTypedReturn<T>(this string text) where T : class
        {
            if(!String.IsNullOrEmpty(text))
                return JsonConvert.DeserializeObject<T>(text)!;

            return null;
        }
    }

    public class RetornoDto<T>
    {
        public string Message { get; set; }
        public bool Success { get; set; } = false;

        [JsonIgnore]
        public int StatusCode { get; set; }
        public T? Object { get; set; }

    }
}