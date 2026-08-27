using cliqx.auth.api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProAuth.Services.Contracts;

namespace cliqx.auth.api.Controllers.v2
{
    [Route("api/v2/oauth")]
    [ApiController]
    public class OAuth : ControllerBase
    {
        public OAuth(IUserService UserService)
        {
            _userService = UserService;
        }

        public IUserService _userService { get; }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> LoginOAuth(UserLoginDto user)
        {
            var retorno = await _userService.LoginUser(user);

            if (retorno.Success)
            {
                return Ok(retorno);
            }
            else
            {
                return this.StatusCode(retorno.StatusCode, retorno);
            }
        }

        [AllowAnonymous]
        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken(TokenDto token)
        {
            var retorno = await this._userService.RefreshToken(token);

            if (retorno.Success)
            {
                return Ok(retorno);
            }
            else
            {
                return this.StatusCode(retorno.StatusCode, retorno);
            }
        }
    }
}