using Newtonsoft.Json;

namespace Cliqx.BuildingBlocks.GlobalModels
{
    public partial class MensagemInterativa
    {
        [JsonProperty("message")]
        public Message Message { get; set; }

    }

    public partial class Message
    {
        [JsonProperty("to")]
        public string To { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("media_id")]
        public string MediaId { get; set; }

        [JsonProperty("interactive")]
        public Interactive Interactive { get; set; }

    }

    public partial class Interactive
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("header")]
        public Header Header { get; set; }

        [JsonProperty("body")]
        public Body Body { get; set; }

        [JsonProperty("footer")]
        public Footer Footer { get; set; }

        [JsonProperty("action")]
        public Action Action { get; set; }

    }

    public partial class Header
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("document")]
        public Document Document { get; set; }

        [JsonProperty("video")]
        public Video Video { get; set; }

        [JsonProperty("image")]
        public Image Image { get; set; }

    }

    public partial class Document
    {
        [JsonProperty("link")]
        public string Link { get; set; }

        [JsonProperty("provider")]
        public Provider Provider { get; set; }

        [JsonProperty("filename")]
        public string Filename { get; set; }

    }

    public partial class Video
    {
        [JsonProperty("link")]
        public string Link { get; set; }

        [JsonProperty("provider")]
        public Provider Provider { get; set; }

    }

    public partial class Image
    {
        [JsonProperty("link")]
        public string Link { get; set; }

        [JsonProperty("provider")]
        public Provider Provider { get; set; }

    }

    public partial class Provider
    {
        [JsonProperty("name")]
        public string Name { get; set; }

    }

    public partial class Body
    {
        [JsonProperty("text")]
        public string Text { get; set; }

    }

    public partial class Footer
    {
        [JsonProperty("text")]
        public string Text { get; set; }

    }

    public partial class Action
    {
        [JsonProperty("buttons")]
        public List<Buttons> Buttons { get; set; }

        [JsonProperty("button")]
        public string Button { get; set; }

        [JsonProperty("sections")]
        public List<Sections> Sections { get; set; }

    }

    public partial class Sections
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("rows")]
        public List<Rows> Rows { get; set; }

    }

    public partial class Rows
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    public partial class Buttons
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("reply")]
        public Reply Reply { get; set; }

    }

    public partial class Reply
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

    }

}
