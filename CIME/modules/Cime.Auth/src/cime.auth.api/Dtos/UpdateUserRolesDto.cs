using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace cliqx.auth.api.Dtos
{
    // Sincroniza os cargos de um usuário: os cargos informados passam a ser o
    // conjunto exato do usuário (adiciona os que faltam, remove os que sobram).
    public class UpdateUserRolesDto
    {
        [Required]
        public int UserId { get; set; }
        public List<string> Roles { get; set; } = new();
    }
}
