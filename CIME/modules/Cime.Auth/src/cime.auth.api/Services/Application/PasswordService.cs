using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using cliqx.auth.api.Dtos;
using cliqx.auth.api.Models;
using cliqx.auth.api.Models.Identity;
using cliqx.auth.api.Models.Types;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ProAuth.Services.Contracts;
using ProSales.Repository.Contexts;

namespace cliqx.auth.api.Services.Application;

public class PasswordService : IPasswordService
{
    private readonly DefaultContext context;
    public readonly UserManager<User> _userManager;
    public readonly SignInManager<User> _signInManager;
    public readonly RoleManager<Role> _roleInManager;
    public readonly IMapper _mapper;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;

    public PasswordService(DefaultContext context, UserManager<User> userManager, SignInManager<User> signInManager, RoleManager<Role> roleInManager, IMapper mapper, IEmailService emailService, IConfiguration config)
    {
        this.context = context;
        _userManager = userManager;
        _signInManager = signInManager;
        _roleInManager = roleInManager;
        _mapper = mapper;
        _emailService = emailService;
        _config = config;
    }

    public async Task<RetornoDto> ValidateEmailByUsernameAndEmail(string username, string email)
    {
        var retorno = new RetornoDto();
        try
        {
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

            if (userFound.Email != email)
            {
                retorno.Message = "Email inválido";
                retorno.StatusCode = StatusCodes.Status406NotAcceptable;
                return retorno;
            }

            retorno.Message = "Email válido";
            retorno.Success = true;

            return retorno;
        }
        catch (System.Exception e)
        {
            retorno.Object = e.Message;
            return retorno;
        }


    }

    public async Task<RetornoDto> ValidateForgetCodeForUserId(int userId, string code)
    {
        var retorno = new RetornoDto();

        try
        {
            var codeFound = await this.context.UserForgetCodes.AsNoTracking().AsQueryable()
                .Where(x => x.UserId == userId && x.ForgetCode == code).FirstOrDefaultAsync();

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

            retorno.Success = true;
            retorno.Message = "Código válido";
            return retorno;
        }
        catch (System.Exception e)
        {
            retorno.Object = e.Message;
            return retorno;
        }
    }

    public async Task<RetornoDto> ValidateForgetCodeForUsername(string username, string code)
    {
        var retorno = new RetornoDto();

        var userFound = await _userManager.Users
            .FirstOrDefaultAsync(u => u.NormalizedUserName == username.ToUpper());

        if (userFound == null)
        {
            retorno.Message = "Usuario não encontrado";
            retorno.StatusCode = StatusCodes.Status404NotFound;
            return retorno;
        }

        return await ValidateForgetCodeForUserId(userFound.Id, code);
    }

    public async Task<RetornoDto> GenerateForgetCodeForUserName(string userName, TypeCodeEnum type)
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


            // Invalida códigos anteriores do usuário: apenas o link mais recente vale
            // e um código já usado/antigo não pode ser reaproveitado.
            var oldCodes = await this.context.UserForgetCodes
                .Where(x => x.UserId == userFound.Id).ToListAsync();
            if (oldCodes.Count > 0)
                this.context.UserForgetCodes.RemoveRange(oldCodes);

            Random random = new Random();
            string randomString = "SVC-" + random.Next(0, 1000000).ToString("D6");

            var expirationMinutes = int.TryParse(_config["App:ForgetCodeExpirationMinutes"], out var m) ? m : 60;

            var userForgetCode = new UserForgetCode();
            userForgetCode.UserId = userFound.Id;
            userForgetCode.ForgetCode = randomString;
            userForgetCode.ExpirationDate = DateTime.Now.AddMinutes(expirationMinutes);



            this.context.Add(userForgetCode);

            if (await this.context.SaveChangesAsync() > 0)
            {
                retorno.Success = true;
                retorno.Message = "Código enviado com sucesso para o email cadastrado";
                retorno.StatusCode = StatusCodes.Status201Created;

                // Envia o e-mail correto conforme o tipo (boas-vindas x redefinição).
                if (type == TypeCodeEnum.RegisterNewUser)
                    _ = _emailService.SendFirstAccessEmail(userFound, userForgetCode.ForgetCode, userForgetCode.ExpirationDate);
                else
                    _ = _emailService.SendResetPasswordEmail(userFound, userForgetCode.ForgetCode, userForgetCode.ExpirationDate);

                return retorno;
            }

            retorno.Message = "Erro ao salvar";
            return retorno;
        }
        catch (System.Exception e)
        {
            retorno.Object = e.Message;
            return retorno;
        }
    }

    public async Task<RetornoDto> UpdatePassword(UserLoginDto userDto, string code)
    {
        var retorno = new RetornoDto();

        try
        {
            var user = _mapper.Map<User>(userDto);
            var userFound = await _userManager.FindByNameAsync(user.UserName);

            var codeFound = await this.ValidateForgetCodeForUserId(userFound.Id, code);

            if (!codeFound.Success)
                return codeFound;


            if (userFound == null)
            {
                retorno.Message = "Usuário não encontrado!";
                retorno.StatusCode = StatusCodes.Status404NotFound;
                return retorno;
            }

            if (String.IsNullOrEmpty(userDto.Password))
            {
                retorno.Message = "Senha não preenchida!";
                retorno.StatusCode = StatusCodes.Status404NotFound;
                return retorno;
            }



            if (!verifyPasswordRules(userDto.Password).Success)
            {
                return verifyPasswordRules(userDto.Password);
            }

            userFound.PasswordHash = _userManager.PasswordHasher.HashPassword(userFound, userDto.Password);

            // Definir a senha por código também confirma o e-mail (fluxo de primeiro acesso).
            userFound.EmailConfirmed = true;

            var appUser = await _userManager.UpdateAsync(userFound);

            if (appUser.Succeeded)
            {
                // Consome o(s) código(s) usado(s) para que não seja reutilizado.
                var usedCodes = await this.context.UserForgetCodes
                    .Where(x => x.UserId == userFound.Id && x.ForgetCode == code)
                    .ToListAsync();
                if (usedCodes.Count > 0)
                {
                    this.context.UserForgetCodes.RemoveRange(usedCodes);
                    await this.context.SaveChangesAsync();
                }

                var userToReturn = _mapper.Map<UserDto>(user);

                retorno.Object = userToReturn;
                retorno.Success = true;
                retorno.Message = "Senha atualizada com sucesso";
                return retorno;
            }

            throw new Exception(appUser.Errors.ToString());

        }
        catch (System.Exception e)
        {
            retorno.Object = e.Message;
            retorno.StatusCode = StatusCodes.Status500InternalServerError;
            return retorno;
        }
    }

    private RetornoDto verifyPasswordRules(string password)
    {
        var retorno = new RetornoDto();
        var passwordOptions = _userManager.Options.Password;

        if (passwordOptions.RequireDigit)
        {
            var hasDigit = password.Any(char.IsDigit);

            if (!hasDigit)
            {
                retorno.Message = "A senha deve conter pelo menos um dígito!";
                retorno.StatusCode = StatusCodes.Status400BadRequest;
                return retorno;
            }
        }

        if (passwordOptions.RequireNonAlphanumeric)
        {
            var hasNonAlphaNumeric = password.Any(c => !char.IsLetterOrDigit(c));

            if (!hasNonAlphaNumeric)
            {
                retorno.Message = "A senha deve conter pelo menos um caractere não alfanumérico!";
                retorno.StatusCode = StatusCodes.Status400BadRequest;
                return retorno;
            }
        }

        if (passwordOptions.RequireLowercase)
        {
            var hasLower = password.Any(char.IsLower);

            if (!hasLower)
            {
                retorno.Message = "A senha deve conter pelo menos uma letra minúscula!";
                retorno.StatusCode = StatusCodes.Status400BadRequest;
                return retorno;
            }
        }

        if (passwordOptions.RequireUppercase)
        {
            var hasUpper = password.Any(char.IsUpper);

            if (!hasUpper)
            {
                retorno.Message = "A senha deve conter pelo menos uma letra maiúscula!";
                retorno.StatusCode = StatusCodes.Status400BadRequest;
                return retorno;
            }
        }

        if (password.Length < passwordOptions.RequiredLength)
        {
            retorno.Message = $"A senha deve ter pelo menos {passwordOptions.RequiredLength} caracteres!";
            retorno.StatusCode = StatusCodes.Status400BadRequest;
            return retorno;
        }

        retorno.Success = true;
        return retorno;
    }


}
