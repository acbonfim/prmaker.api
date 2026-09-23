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
using cliqx.auth.api.Security;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace cliqx.auth.api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UserController : ControllerBase
{

    public IUserService _userService { get; }
    public IHttpContextAccessor _accessor { get; }

    public UserController(IUserService UserService, IHttpContextAccessor accessor)
    {
        _userService = UserService;
        _accessor = accessor;
    }

    [Authorize("Bearer", Roles = "admin")]
    [HttpPost("Register")]
    public async Task<IActionResult> Register(UserDto user)
    {
        var retorno = await _userService.RegisterUser(user);

        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }

    [AllowAnonymous]
    [HttpGet("ConfirmEmail")]
    public async Task<IActionResult> ConfirmEmail(
        [FromQuery]
        string username, [FromQuery]string code)
    {
        var retorno = await this._userService.ConfirmEmail(username,code);
        
        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }

    [AllowAnonymous]
    [HttpPost("Login")]
    public async Task<IActionResult> Login(UserLoginDto user)
    {
        var retorno = await _userService.LoginUser(user);

        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }

    [HttpGet("generate-api-key")]
    public async Task<IActionResult> GenerateApiKey()
    {
        var username = _accessor.HttpContext.User.Claims.First(x => x.Type == ClaimTypes.Name).Value;
        var retorno = await _userService.GenerateApiKey(username);

        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }

    [AllowAnonymous]
    [HttpPost("PhotosByExternalIds")]
    public async Task<IActionResult> PhotosByExternalIds([FromBody] string[] externalIds)
    {
        var retorno = await this._userService.GetPhotosByExternalIds(externalIds?.ToList() ?? new List<string>());

        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }

    [AllowAnonymous]
    [HttpGet("is-user-active")]
    public async Task<IActionResult> IsUserActive([FromQuery] string username)
    {
        var retorno = await _userService.IsUserActive(username);

        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }


    [AllowAnonymous]
    [HttpPost("RefreshToken")]
    public async Task<IActionResult> RefreshToken(TokenDto token)
    {
        var retorno = await this._userService.RefreshToken(token);
        
        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }

    [Authorize("Bearer", Roles = "admin")]
    [HttpPost("GetAll")]
    public async Task<IActionResult> GetAll([FromQuery] int page,[FromQuery] [Range(1,100)] int itemsPerPage, [FromQuery] string? search = null)
    {
        var retorno = await this._userService.GetAllUsers(page,itemsPerPage, search);

        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }

    [Authorize("Bearer", Roles = "admin")]
    [HttpPatch("ActiveToggle")]
    public async Task<IActionResult> ActiveToggle([FromQuery] int userId, [FromQuery] bool isActive)
    {
        var retorno = await this._userService.ActiveToggle(userId, isActive);

        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }

    [Authorize("Bearer", Roles = "admin")]
    [HttpPut("Update")]
    public async Task<IActionResult> Update(UpdateUserDto user)
    {
        var retorno = await this._userService.UpdateUser(user);

        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }

    [Authorize("Bearer", Roles = "admin")]
    [HttpPut("UpdateRoles")]
    public async Task<IActionResult> UpdateRoles(UpdateUserRolesDto dto)
    {
        var retorno = await this._userService.UpdateUserRoles(dto.UserId, dto.Roles);

        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }

    [Authorize("Bearer")]
    [HttpPost("ChangePassword")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        var username = _accessor.HttpContext.User.Claims.First(x => x.Type == ClaimTypes.Name).Value;
        var retorno = await this._userService.ChangePassword(username, dto.CurrentPassword, dto.NewPassword);

        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }

    [Authorize("Bearer")]
    [HttpPost("UpdatePhoto")]
    public async Task<IActionResult> UpdatePhoto(UpdatePhotoDto dto)
    {
        var username = _accessor.HttpContext.User.Claims.First(x => x.Type == ClaimTypes.Name).Value;
        var retorno = await this._userService.UpdatePhoto(username, dto.ImageUrl);

        if(retorno.Success){
            return Ok(retorno);
        }else
        {
            return this.StatusCode(retorno.StatusCode,retorno);
        }
    }
}
