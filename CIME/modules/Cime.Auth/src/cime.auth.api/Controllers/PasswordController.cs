using System.ComponentModel.DataAnnotations;
using cliqx.auth.api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProAuth.Services.Contracts;

namespace cliqx.auth.api.Controllers;

[Route("api/[controller]/[action]")]
[ApiController]
public class PasswordController : ControllerBase
{
    private readonly IPasswordService _passwordService;
    public PasswordController(IPasswordService passwordService)
    {
        _passwordService = passwordService;
    }


    /// <summary>
    /// Vai gerar um código que será valido por 5 minutos e vai garantir a possibilidade de alterar a senha. Esse código chega no e-mail cadastrado do cliente
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GenerateForgetCodeByUsername([FromQuery] string userName)
    {
        var retorno = await _passwordService.GenerateForgetCodeForUserName(userName, Models.Types.TypeCodeEnum.ForgetPassword);

        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }

    /// <summary>
    /// Atualiza a senha do cliente, validando se o código informado é válido
    /// </summary>
    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> UpdatePassword(UserLoginDto user, [FromQuery]string code)
    {
        var retorno = await _passwordService.UpdatePassword(user,code);

        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }
}
