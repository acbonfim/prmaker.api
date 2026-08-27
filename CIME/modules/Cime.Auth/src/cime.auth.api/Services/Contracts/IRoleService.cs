using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using cliqx.auth.api.Dtos;

namespace ProAuth.Services.Contracts;

public interface IRoleService
{
    Task<RetornoDto> CreateRole (CreateRoleDto Role);
    Task<RetornoDto> GetAllRoles ();
}
