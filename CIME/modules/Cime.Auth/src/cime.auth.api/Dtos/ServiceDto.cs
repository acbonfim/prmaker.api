namespace cliqx.auth.api.Dtos
{
    // DTO explícito de serviço. Evita depender da serialização da entidade MyService,
    // que sob Newtonsoft ignora os atributos System.Text.Json do BasicEntity e vaza
    // o Id numérico como "id". Aqui expomos SEMPRE o externalId (string) separado do Id.
    public class ServiceDto
    {
        public string ExternalId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
