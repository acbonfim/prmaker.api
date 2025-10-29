using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using cliqx.auth.api.Dtos;
using cliqx.auth.api.Models.Types;

namespace ProAuth.Services.Contracts;

public interface IPasswordService
{
    Task<RetornoDto> ValidateEmailByUsernameAndEmail (string username, string email);
    Task<RetornoDto> ValidateForgetCodeForUserId (int userId, string code);
    Task<RetornoDto> GenerateForgetCodeForUserName (string userName, TypeCodeEnum type);
    Task<RetornoDto> UpdatePassword (UserLoginDto user,string code);
    
}
