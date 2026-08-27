using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ProAuth.Services.Contracts;
using cliqx.auth.api.Dtos;

namespace cliqx.auth.api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class RoleController : ControllerBase
{

    public IRoleService _roleService { get; }

    public RoleController(IRoleService RoleService)
    {
        _roleService = RoleService;
    }

    [Authorize("Bearer", Roles = "admin")]
    [HttpPost("CreateRole")]
    public async Task<IActionResult> CreateRole(CreateRoleDto role)
    {
        var retorno = await _roleService.CreateRole(role);

        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }

    [Authorize("Bearer", Roles = "admin")]
    [HttpGet("GetAll")]
    public async Task<IActionResult> GetAll()
    {
        var retorno = await _roleService.GetAllRoles();

        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }
}
