using System.ComponentModel;
using System.Text.Json.Serialization;

namespace cliqx.auth.api.Models
{
    public class BasicEntity : ICustomId
    {


        public BasicEntity()
        {
            if (string.IsNullOrEmpty(ExternalId))
            {
                ExternalId = GenerateCustomExternalId();
            }
        }

        [JsonIgnore]
        public long Id { get; set; }

        [JsonPropertyName("id")]
        public string ExternalId { get; set; }

        public bool IsActive { get; set; } = true;

        [JsonIgnore]
        public DateTime CreatedAt { get; set; } = DateTime.Now;



        private string GenerateCustomExternalId()
        {
            var name = this.GetType().Name;

            var attribute = (DisplayNameAttribute)Attribute.GetCustomAttribute(this.GetType(), typeof(DisplayNameAttribute));

            if (attribute != null)
            {
                name = attribute.DisplayName;
            }
            else
            {
                name = name.Substring(0, 3).ToUpper();
            }


            var guid = Guid.NewGuid().ToString().Replace(".", "").Substring(0, 6).ToUpper();

            var random = new Random();
            var numeros = (random.Next(1, 90000000)).ToString("D8");

            return $"{name}{numeros}{guid}";
        }
    }
}