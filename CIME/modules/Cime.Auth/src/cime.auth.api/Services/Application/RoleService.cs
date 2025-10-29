using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using ProAuth.Services.Contracts;
using cliqx.auth.api.Dtos;
using cliqx.auth.api.Models.Identity;

namespace ProAuth.Services.Application;

public class RoleService : IRoleService
{
    public UserManager<User> _userManager { get; }
    public SignInManager<User> _signInManager { get; }
    public RoleManager<Role> _roleInManager { get; }
    public IMapper _mapper { get; }

    public RoleService(UserManager<User> UserManager
    , SignInManager<User> signInManager
    , RoleManager<Role> roleInManager
    , IMapper mapper
    )
    {
        _userManager = UserManager;
        _signInManager = signInManager;
        _roleInManager = roleInManager;
        _mapper = mapper;
    }
    public async Task<RetornoDto> CreateRole(CreateRoleDto roleDto)
    {
        var retorno = new RetornoDto();
        var role = _mapper.Map<Role>(roleDto);

        role.Name = roleDto.Name;

        try
        {
            var roleFound = await _roleInManager.FindByNameAsync(role.Name);

            if(roleFound != null) 
            {
                retorno.Message = "Role já cadastrada!";
                retorno.StatusCode = StatusCodes.Status409Conflict;
                retorno.Object = roleFound;
                return retorno;
            }

            var createdRole = await _roleInManager.CreateAsync(role);

            if(createdRole.Succeeded)
            {
                retorno.Success = true;
                retorno.Message = "Role criada com sucesso!";
                retorno.Object = await _roleInManager.FindByNameAsync(role.Name);
            }else
            {
                retorno.Object = createdRole.Errors;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            retorno.Success = false;
            retorno.Message = "Erro ao tentar adicionar usuário";
            retorno.Object = ex.StackTrace;
        }

        return retorno;
    }

    public async Task<RetornoDto> GetAllRoles()
    {
        var retorno = new RetornoDto();

        try
        {
            var roles =  _roleInManager.Roles;

            retorno.Object = roles;
            retorno.Success = true;
            retorno.StatusCode = 200;
            retorno.Message = "Busca realizada com sucesso";

            return retorno;

        }
        catch (System.Exception e)
        {
            retorno.Message = $"{e.Message}";
            retorno.Success = false;
            retorno.StatusCode = 500;
            return retorno;
        }
    }
}
