using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.JsonPatch.Internal;

namespace cliqx.auth.api.Models.Identity
{
    public class Role : IdentityRole<int>
    {
        public List<UserRole>? UserRoles { get; set; }
        public Guid ExternalId { get; set; } = Guid.NewGuid();
        public int Level { get; set; }

        public void salvarCargo(int idCargo)
        {
            var listCargos = new List<Role>();

            var admin = listCargos.First();
            var externalCliente = listCargos.Last();

            
        }

    }
}