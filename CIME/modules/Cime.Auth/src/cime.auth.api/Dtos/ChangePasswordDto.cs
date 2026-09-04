using System.ComponentModel.DataAnnotations;

namespace cliqx.auth.api.Dtos
{
    // Troca de senha do próprio usuário autenticado (informa a senha atual).
    public class ChangePasswordDto
    {
        [Required]
        public string CurrentPassword { get; set; }

        [Required]
        public string NewPassword { get; set; }
    }
}
