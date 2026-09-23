using System.ComponentModel.DataAnnotations;

namespace cliqx.auth.api.Dtos
{
    // Atualização de dados de perfil de um usuário existente (tela de gestão).
    public class UpdateUserDto
    {
        [Required]
        public int Id { get; set; }
        public string FullName { get; set; }

        [EmailAddress]
        public string Email { get; set; }
        public string Departamento { get; set; }
    }
}
