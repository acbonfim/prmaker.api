using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using cliqx.auth.api.Dtos;

namespace ProAuth.Services.Contracts;

public interface IUserService
{
    Task<RetornoDto> RegisterUser (UserDto User);
    Task<RetornoDto> LoginUser (UserLoginDto User);
    Task<RetornoDto> GenerateApiKey(string username);
    Task<RetornoDto> IsUserActive(string username);
    Task<RetornoDto> RefreshToken(TokenDto tokenDto);
    Task<RetornoDto> ConfirmEmail(string userName, string code);
    Task<RetornoDto> GetAllUsers(int page, int itemsPerPage);
    
}
