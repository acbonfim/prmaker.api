using System.Text;
using Newtonsoft.Json;

namespace Cime.BuildingBlocks.GlobalExtensions
{
    public static class ByteArrayExtensions
    {
        public static byte[] AsByteArrayFromString(this string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        public static byte[] AsByteArrayFromObject<T>(this T obj)
        {
            var json = JsonConvert.SerializeObject(obj);
            return json.AsByteArrayFromString();
        }
    }
}