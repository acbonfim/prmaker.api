using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace cliqx.auth.api.Dtos
{
    public class UserDto
    {
        public string UserName { get; set; }
        
        [EmailAddress]
        public string Email { get; set; }
        public string Password { get; set; }
        public string FullName { get; set; }
        public DateTime DataNascimento { get; set; }
        public string Departamento { get; set; }
        // Campos não enviados pelo cliente no cadastro: anuláveis para não virarem
        // "required" pelo binding (Nullable habilitado). SecurityStamp é interno do Identity.
        public string? ImagemUrlUser { get; set; }
        public string Role { get; set; }
        public string? SecurityStamp { get; set; }
        public int CompanyId { get; set; }
        public Guid ExternalId { get; set; } = Guid.NewGuid();
        
        [Required]
        public string ChannelOrigin { get; set; }

    }
}