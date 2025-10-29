using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using ProAuth.Services.Contracts;

namespace cliqx.auth.api.Controllers.v2
{
    [ApiController]
    [Route("api/v2/[controller]")]
    public class IntegrationController : ControllerBase
    {
        private readonly IHttpContextAccessor _accessor;
        private readonly IUserService _userService;

        public IntegrationController(
            IHttpContextAccessor accessor
            , IUserService UserService
        )
        {
            _accessor = accessor;
            _userService = UserService;
        }

        [HttpGet("key/generate")]
        public async Task<IActionResult> GenerateApiKey()
        {
            var username = _accessor.HttpContext.User.Claims.First(x => x.Type == ClaimTypes.Name).Value;
            var retorno = await _userService.GenerateApiKey(username);

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