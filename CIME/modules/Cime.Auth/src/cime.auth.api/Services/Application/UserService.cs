using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProAuth.Services.Contracts;
using cliqx.auth.api.Dtos;
using cliqx.auth.api.Models.Identity;
using cliqx.auth.api.Security;
using ProSales.Repository.Contexts;
using cliqx.auth.api.Services;
using cliqx.auth.api.Models.Types;
using Microsoft.AspNetCore.Authorization;

namespace ProAuth.Services.Application;

public class UserService : IUserService
{
    public UserManager<User> _userManager { get; }
    public SignInManager<User> _signInManager { get; }
    public RoleManager<Role> _roleInManager { get; }
    public IMapper _mapper { get; }
    public readonly DefaultContext context;
    private readonly IPasswordService _passService;

    public UserService(UserManager<User> UserManager
    , SignInManager<User> signInManager
    , RoleManager<Role> roleInManager
    , IMapper mapper
    , DefaultContext context,
IPasswordService passService)
    {
        _userManager = UserManager;
        _signInManager = signInManager;
        _roleInManager = roleInManager;
        _mapper = mapper;
        this.context = context;
        _passService = passService;
    }
    public async Task<RetornoDto> RegisterUser(UserDto userDto)
    {
        var retorno = new RetornoDto();

        try
        {
            var user = _mapper.Map<User>(userDto);

            var userFound = await _userManager.FindByNameAsync(user.UserName);

            if (userFound != null)
            {
                retorno.Message = "Usuário já cadastrado!";
                retorno.StatusCode = StatusCodes.Status409Conflict;
                retorno.Object = userFound;
                return retorno;
            }

            var role = await _roleInManager.FindByNameAsync(userDto.Role);

            if (role == null)
            {
                retorno.Message = "Role não encontrada";
                retorno.StatusCode = StatusCodes.Status404NotFound;
                return retorno;
            }

            user.FullName = user.FullName.Trim();
            user.UserName = user.UserName.Trim();

            var result = await _userManager.CreateAsync(user, userDto.Password);


            if (role != null) await _userManager.AddToRoleAsync(user, userDto.Role);

            if (result.Succeeded)
            {
                await _passService.GenerateForgetCodeForUserName(user.UserName, TypeCodeEnum.RegisterNewUser);
                retorno.Success = true;
                retorno.Message = "Usuário criado com sucesso!";
                retorno.Object = await _userManager.FindByNameAsync(user.UserName);
            }
            else
            {
                retorno.Object = result.Errors;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.StackTrace);
            retorno.Success = false;
            retorno.Message = "Erro ao tentar adicionar usuário";
            retorno.Object = ex.Message;
        }

        return retorno;
    }

    public async Task<RetornoDto> GenerateApiKey(string username)
    {
        var retorno = new RetornoDto();

        var userFound = await _userManager.Users
            .FirstOrDefaultAsync(u => u.NormalizedUserName == username.ToUpper());

        if (userFound is null)
        {
            retorno.Message = "Usuario não encontrado";
            retorno.StatusCode = StatusCodes.Status404NotFound;
            return retorno;
        }

        if (userFound.Active == false)
        {
            retorno.Message = "Usuario desativado";
            retorno.StatusCode = StatusCodes.Status403Forbidden;
            return retorno;
        }

        var passwordOptions = _userManager.Options.SignIn;

        if (passwordOptions.RequireConfirmedEmail)
        {
            if (!userFound.EmailConfirmed)
            {
                retorno.Message = "E-mail não confirmado!";
                retorno.StatusCode = StatusCodes.Status403Forbidden;
                return retorno;
            }
        }

        var roles = await _userManager.GetRolesAsync(userFound);
        var services = await this.context.UserServices
            .Include(x => x.Service)
            .Select(x => new {x.Service.Name,x.Service.Description, x.UserId, x.Service.ExternalId})
                .Where(x => x.UserId == userFound.Id).ToListAsync();

        var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userFound.Id.ToString()),
                new Claim(ClaimTypes.Name, userFound.UserName),
                new Claim("ExternalId", userFound.ExternalId.ToString())
            };

        foreach (var service in services)
        {
            claims.Add(new Claim("Services", service.ExternalId));
        }

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var userToReturn = _mapper.Map<UserDto>(userFound);

        var retornoToken = new
        {
            apiKey = TokenService.GenerateAccessToken(claims, true),
            user = userToReturn,
            roles = roles,
            services = services,
        };

        

        retorno.Success = true;
        retorno.Message = "Api Key gerada com sucesso";
        retorno.StatusCode = StatusCodes.Status200OK;
        retorno.Object = retornoToken;


        return retorno;
    }

    public async Task<RetornoDto> LoginUser(UserLoginDto userLogin)
    {
        var retorno = new RetornoDto();

        try
        {
            var passwordOptions = _userManager.Options.SignIn;



            var userFound = await _userManager.Users
                .FirstOrDefaultAsync(u => u.NormalizedUserName == userLogin.UserName.ToUpper());

            if (userFound == null)
            {
                retorno.Message = "Usuario não encontrado";
                retorno.StatusCode = StatusCodes.Status404NotFound;
                return retorno;
            }

            if (userFound.Active == false)
            {
                retorno.Message = "Usuario desativado";
                retorno.StatusCode = StatusCodes.Status403Forbidden;
                return retorno;
            }

            if (passwordOptions.RequireConfirmedEmail)
            {
                if (!userFound.EmailConfirmed)
                {
                    retorno.Message = "E-mail não confirmado!";
                    retorno.StatusCode = StatusCodes.Status403Forbidden;
                    return retorno;
                }
            }

            var passWordOk = await _userManager.CheckPasswordAsync(userFound, userLogin.Password);

            if (passWordOk)
            {

                var userToReturn = _mapper.Map<UserDto>(userFound);
                var roles = await _userManager.GetRolesAsync(userFound);

                userFound.DataUltimoLogin = DateTime.Now;

                await _userManager.UpdateAsync(userFound);

                var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userFound.Id.ToString()),
                new Claim(ClaimTypes.Name, userFound.UserName),
                new Claim("ExternalId", userFound.ExternalId.ToString())
            };

                foreach (var role in roles)
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }

                var retornoToken = new
                {
                    accessToken = TokenService.GenerateAccessToken(claims),
                    refreshToken = TokenService.GenerateRefreshToken(claims),
                    user = userToReturn,
                    roles = roles
                };

                retorno.Success = true;
                retorno.Message = "Login realizado com sucesso";
                retorno.StatusCode = StatusCodes.Status200OK;
                retorno.Object = retornoToken;
            }
            else
            {
                retorno.Success = false;
                retorno.Message = "Senha incorreta";
                retorno.StatusCode = StatusCodes.Status401Unauthorized;
            }
        }
        catch (System.Exception ex)
        {
            Console.WriteLine(ex.StackTrace);
            retorno.Success = false;
            retorno.Message = "Erro ao tentar realizar login";
            retorno.StatusCode = StatusCodes.Status500InternalServerError;
            retorno.Object = ex.Message;
        }


        return retorno;
    }

    public async Task<RetornoDto> RefreshToken(TokenDto tokenDto)
    {
        var retorno = new RetornoDto();

        retorno.StatusCode = StatusCodes.Status404NotFound;
        retorno.Success = false;

        if (tokenDto is null)
        {
            retorno.Message = "Token null";
            return retorno;
        }

        string accessToken = tokenDto.AccessToken;
        string refreshToken = tokenDto.RefreshToken;


        try
        {
            var principal = await TokenService.GetPrincipalFromExpiredToken(refreshToken);

            var username = principal.Identity.Name;

            var userFound = await _userManager.Users
                .FirstOrDefaultAsync(u => u.NormalizedUserName == username.ToUpper());

            if (userFound == null)
            {
                retorno.Message = "Usuario não encontrado";
                retorno.StatusCode = StatusCodes.Status404NotFound;
                return retorno;
            }

            if (userFound.Active == false)
            {
                retorno.Message = "Usuario desativado";
                retorno.StatusCode = StatusCodes.Status403Forbidden;
                return retorno;
            }

            var newAccessToken = TokenService.GenerateAccessToken(principal.Claims);
            var newRefreshToken = TokenService.GenerateRefreshToken(principal.Claims);


            var newToken = new TokenDto()
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken
            };

            retorno.Success = true;
            retorno.StatusCode = 200;
            retorno.Object = newToken;
            retorno.Message = "Access token gerado com sucesso";


        }
        catch (SecurityTokenException ex)
        {
            Console.WriteLine(ex.StackTrace);
            retorno.StatusCode = StatusCodes.Status403Forbidden;
            retorno.Success = false;
            retorno.Message = "Erro ao tentar realizar atualizar o token. Token invalido";
            retorno.Object = ex.Message;
        }
        catch (System.Exception ex)
        {
            Console.WriteLine(ex.StackTrace);
            retorno.StatusCode = StatusCodes.Status500InternalServerError;
            retorno.Success = false;
            retorno.Message = "Erro ao tentar realizar atualizar o token";
            retorno.Object = ex.Message;
        }


        return retorno;
    }

    public async Task<RetornoDto> ConfirmEmail(string userName, string code)
    {
        var retorno = new RetornoDto();

        try
        {

            var userFound = await _userManager.Users
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == userName.ToLower());

            if (userFound == null)
            {
                retorno.Message = "Usuario não encontrado";
                retorno.StatusCode = StatusCodes.Status404NotFound;
                return retorno;
            }

            if (userFound.Active == false)
            {
                retorno.Message = "Usuario desativado";
                retorno.StatusCode = StatusCodes.Status403Forbidden;
                return retorno;
            }

            var codeFound = await this.context.UserForgetCodes.AsNoTracking().AsQueryable()
                .Where(x => x.UserId == userFound.Id && x.ForgetCode == code).FirstOrDefaultAsync();

            if (codeFound == null)
            {
                retorno.Message = "Codigo não encontrado";
                retorno.StatusCode = StatusCodes.Status404NotFound;
                return retorno;
            }

            if (DateTime.Now > codeFound.ExpirationDate)
            {
                retorno.Message = "Codigo expirado";
                retorno.StatusCode = StatusCodes.Status403Forbidden;
                return retorno;
            }



            userFound.EmailConfirmed = true;

            var appUser = await _userManager.UpdateAsync(userFound);

            if (!appUser.Succeeded)
            {
                retorno.StatusCode = StatusCodes.Status500InternalServerError;
                retorno.Success = true;
                retorno.Object = appUser.Errors;

                return retorno;
            }

            retorno.StatusCode = StatusCodes.Status200OK;
            retorno.Message = "E-mail confirmado com sucesso";
            retorno.Success = true;
            retorno.Object = _mapper.Map<UserDto>(userFound);

            return retorno;
        }
        catch (System.Exception e)
        {
            retorno.Object = e.Message;
            return retorno;
        }
    }

    public async Task<RetornoDto> GetAllUsers(int page, int itemsPerPage, string? search = null)
    {
        var retorno = new RetornoDto();
        try
        {
            var query = _userManager.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(u =>
                    (u.FullName != null && u.FullName.ToLower().Contains(term))
                    || (u.UserName != null && u.UserName.ToLower().Contains(term))
                    || (u.Email != null && u.Email.ToLower().Contains(term)));
            }

            var count = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)count / itemsPerPage);

            var users = await query
                .OrderBy(u => u.FullName)
                .Skip(page * itemsPerPage)
                .Take(itemsPerPage)
                .ToListAsync();

            var elements = users.Select(u => new UserListItemDto
            {
                Id = u.Id,
                UserName = u.UserName,
                Email = u.Email,
                FullName = u.FullName,
                Departamento = u.Departamento,
                Active = u.Active,
                ExternalId = u.ExternalId,
                CompanyId = u.CompanyId,
                ChannelOrigin = u.ChannelOrigin,
                ImagemUrlUser = u.ImagemUrlUser,
                UserRoles = (u.UserRoles ?? new List<UserRole>())
                    .Where(ur => ur.Role != null)
                    .Select(ur => new UserRoleItemDto
                    {
                        Role = new RoleItemDto { Id = ur.Role.Id, Name = ur.Role.Name }
                    }).ToList()
            }).ToList();

            retorno.Object = new PagedResultDto<UserListItemDto>
            {
                Elements = elements,
                Page = page,
                ItemsPerPage = itemsPerPage,
                Total = count,
                TotalPages = totalPages,
                StillFetchable = (page + 1) * itemsPerPage < count
            };
            retorno.StatusCode = StatusCodes.Status200OK;
            retorno.Success = true;
            return retorno;
        }
        catch (System.Exception e)
        {
            retorno.Message = e.Message;
            retorno.StatusCode = StatusCodes.Status500InternalServerError;
            return retorno;
        }
    }

    public async Task<RetornoDto> ActiveToggle(int userId, bool isActive)
    {
        var retorno = new RetornoDto();
        try
        {
            var userFound = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (userFound is null)
            {
                retorno.Message = "Usuario não encontrado";
                retorno.StatusCode = StatusCodes.Status404NotFound;
                return retorno;
            }

            userFound.Active = isActive;

            var result = await _userManager.UpdateAsync(userFound);

            if (!result.Succeeded)
            {
                retorno.Message = "Erro ao atualizar status do usuário";
                retorno.StatusCode = StatusCodes.Status500InternalServerError;
                retorno.Object = result.Errors;
                return retorno;
            }

            retorno.Success = true;
            retorno.StatusCode = StatusCodes.Status200OK;
            retorno.Message = isActive ? "Usuário ativado" : "Usuário desativado";
            retorno.Object = new { id = userFound.Id, active = userFound.Active };
            return retorno;
        }
        catch (System.Exception e)
        {
            retorno.Message = e.Message;
            retorno.StatusCode = StatusCodes.Status500InternalServerError;
            return retorno;
        }
    }

    public async Task<RetornoDto> UpdateUser(UpdateUserDto userDto)
    {
        var retorno = new RetornoDto();
        try
        {
            var userFound = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userDto.Id);

            if (userFound is null)
            {
                retorno.Message = "Usuario não encontrado";
                retorno.StatusCode = StatusCodes.Status404NotFound;
                return retorno;
            }

            if (!string.IsNullOrWhiteSpace(userDto.FullName))
                userFound.FullName = userDto.FullName.Trim();

            if (!string.IsNullOrWhiteSpace(userDto.Departamento))
                userFound.Departamento = userDto.Departamento.Trim();

            if (!string.IsNullOrWhiteSpace(userDto.Email))
            {
                userFound.Email = userDto.Email.Trim();
                userFound.NormalizedEmail = userDto.Email.Trim().ToUpper();
            }

            var result = await _userManager.UpdateAsync(userFound);

            if (!result.Succeeded)
            {
                retorno.Message = "Erro ao atualizar usuário";
                retorno.StatusCode = StatusCodes.Status500InternalServerError;
                retorno.Object = result.Errors;
                return retorno;
            }

            retorno.Success = true;
            retorno.StatusCode = StatusCodes.Status200OK;
            retorno.Message = "Usuário atualizado com sucesso";
            retorno.Object = _mapper.Map<UserDto>(userFound);
            return retorno;
        }
        catch (System.Exception e)
        {
            retorno.Message = e.Message;
            retorno.StatusCode = StatusCodes.Status500InternalServerError;
            return retorno;
        }
    }

    public async Task<RetornoDto> UpdateUserRoles(int userId, List<string> roles)
    {
        var retorno = new RetornoDto();
        try
        {
            var userFound = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (userFound is null)
            {
                retorno.Message = "Usuario não encontrado";
                retorno.StatusCode = StatusCodes.Status404NotFound;
                return retorno;
            }

            var desired = (roles ?? new List<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Garante que todos os cargos solicitados existem antes de alterar nada.
            foreach (var role in desired)
            {
                if (!await _roleInManager.RoleExistsAsync(role))
                {
                    retorno.Message = $"Cargo '{role}' não encontrado";
                    retorno.StatusCode = StatusCodes.Status404NotFound;
                    return retorno;
                }
            }

            var currentRoles = await _userManager.GetRolesAsync(userFound);

            var toAdd = desired
                .Where(r => !currentRoles.Contains(r, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var toRemove = currentRoles
                .Where(r => !desired.Contains(r, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (toRemove.Any())
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(userFound, toRemove);
                if (!removeResult.Succeeded)
                {
                    retorno.Message = "Erro ao remover cargos";
                    retorno.StatusCode = StatusCodes.Status500InternalServerError;
                    retorno.Object = removeResult.Errors;
                    return retorno;
                }
            }

            if (toAdd.Any())
            {
                var addResult = await _userManager.AddToRolesAsync(userFound, toAdd);
                if (!addResult.Succeeded)
                {
                    retorno.Message = "Erro ao adicionar cargos";
                    retorno.StatusCode = StatusCodes.Status500InternalServerError;
                    retorno.Object = addResult.Errors;
                    return retorno;
                }
            }

            var updatedRoles = await _userManager.GetRolesAsync(userFound);

            retorno.Success = true;
            retorno.StatusCode = StatusCodes.Status200OK;
            retorno.Message = "Cargos atualizados com sucesso";
            retorno.Object = updatedRoles;
            return retorno;
        }
        catch (System.Exception e)
        {
            retorno.Message = e.Message;
            retorno.StatusCode = StatusCodes.Status500InternalServerError;
            return retorno;
        }
    }

    public async Task<RetornoDto> ChangePassword(string username, string currentPassword, string newPassword)
    {
        var retorno = new RetornoDto();
        try
        {
            var userFound = await _userManager.Users
                .FirstOrDefaultAsync(u => u.NormalizedUserName == username.ToUpper());

            if (userFound is null)
            {
                retorno.Message = "Usuario não encontrado";
                retorno.StatusCode = StatusCodes.Status404NotFound;
                return retorno;
            }

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                retorno.Message = "Nova senha não preenchida";
                retorno.StatusCode = StatusCodes.Status400BadRequest;
                return retorno;
            }

            var result = await _userManager.ChangePasswordAsync(userFound, currentPassword, newPassword);

            if (!result.Succeeded)
            {
                retorno.Message = result.Errors.FirstOrDefault()?.Description ?? "Não foi possível alterar a senha";
                retorno.StatusCode = StatusCodes.Status400BadRequest;
                retorno.Object = result.Errors;
                return retorno;
            }

            retorno.Success = true;
            retorno.StatusCode = StatusCodes.Status200OK;
            retorno.Message = "Senha alterada com sucesso";
            return retorno;
        }
        catch (System.Exception e)
        {
            retorno.Message = e.Message;
            retorno.StatusCode = StatusCodes.Status500InternalServerError;
            return retorno;
        }
    }

    public async Task<RetornoDto> UpdatePhoto(string username, string imageUrl)
    {
        var retorno = new RetornoDto();
        try
        {
            var userFound = await _userManager.Users
                .FirstOrDefaultAsync(u => u.NormalizedUserName == username.ToUpper());

            if (userFound is null)
            {
                retorno.Message = "Usuario não encontrado";
                retorno.StatusCode = StatusCodes.Status404NotFound;
                return retorno;
            }

            userFound.ImagemUrlUser = imageUrl;

            var result = await _userManager.UpdateAsync(userFound);

            if (!result.Succeeded)
            {
                retorno.Message = "Erro ao atualizar a foto";
                retorno.StatusCode = StatusCodes.Status500InternalServerError;
                retorno.Object = result.Errors;
                return retorno;
            }

            retorno.Success = true;
            retorno.StatusCode = StatusCodes.Status200OK;
            retorno.Message = "Foto atualizada com sucesso";
            retorno.Object = new { imageUrl = userFound.ImagemUrlUser };
            return retorno;
        }
        catch (System.Exception e)
        {
            retorno.Message = e.Message;
            retorno.StatusCode = StatusCodes.Status500InternalServerError;
            return retorno;
        }
    }

    [AllowAnonymous]
    public async Task<RetornoDto> GetPhotosByExternalIds(List<string> externalIds)
    {
        var retorno = new RetornoDto();
        try
        {
            var guids = (externalIds ?? new List<string>())
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(g => g.HasValue)
                .Select(g => g.Value)
                .Distinct()
                .ToList();

            var users = await _userManager.Users
                .Where(u => guids.Contains(u.ExternalId))
                .Select(u => new
                {
                    externalId = u.ExternalId,
                    fullName = u.FullName,
                    imageUrl = u.ImagemUrlUser
                })
                .ToListAsync();

            retorno.Success = true;
            retorno.StatusCode = StatusCodes.Status200OK;
            retorno.Object = users;
            return retorno;
        }
        catch (System.Exception e)
        {
            retorno.Message = e.Message;
            retorno.StatusCode = StatusCodes.Status500InternalServerError;
            return retorno;
        }
    }

    [AllowAnonymous]
    public async Task<RetornoDto> IsUserActive(string username)
    {
        var retorno = new RetornoDto();

        try
        {
            var userFound = await _userManager.Users.AsQueryable().FirstOrDefaultAsync(x => x.NormalizedUserName.Equals(username.ToUpper()));

            if (userFound == null)
            {
                retorno.Message = "Usuario não encontrado";
                retorno.StatusCode = StatusCodes.Status404NotFound;
                return retorno;
            }

            if (userFound.Active == false)
            {
                retorno.Message = "Usuario desativado";
                retorno.StatusCode = StatusCodes.Status200OK;
                retorno.Success = true;
                retorno.Object = false;
                return retorno;
            }

            retorno.Message = "Usuario ativo";
                retorno.StatusCode = StatusCodes.Status200OK;
                retorno.Success = true;
                retorno.Object = true;
                return retorno;
        }
        catch (System.Exception)
        {
            
            throw;
        }
    }
}
