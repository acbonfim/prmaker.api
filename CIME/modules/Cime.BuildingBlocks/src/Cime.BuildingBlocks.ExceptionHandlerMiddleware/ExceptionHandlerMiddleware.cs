using Cliqx.BuildingBlocks.GlobalModels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Security.Authentication;

namespace Cliqx.BuildingBlocks.ExceptionHandlerMiddleware;
public class ExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private IConfiguration _config;

    public ExceptionHandlerMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration config)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var _ret = new ReturnDto<Error>();

            var settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
            };

            context.Response.ContentType = "application/json";

            Console.WriteLine("********** <exception> ***********");
            Console.WriteLine($"     ");
            Console.WriteLine($"     message: {ex.Message}");
            Console.WriteLine($"     ");
            Console.WriteLine($"     stack trace: {ex.StackTrace}");
            Console.WriteLine($"     ");
            Console.WriteLine("********** </exception> ***********");

            if (ex is DataBaseException)
            {
                _ret = _ret.DatabaseError(ex);
                context.Response.StatusCode = (int)_ret.StatusCode;
                
                await context.Response.WriteAsync(JsonConvert.SerializeObject(_ret, settings));
                return;
            }

            if (ex is InvalidCredentialException)
            {
                _ret = _ret.HmecNotValid();
                context.Response.StatusCode = (int)_ret.StatusCode;
                
                await context.Response.WriteAsync(JsonConvert.SerializeObject(_ret, settings));
                return;
            }


            if (ex is ItemExpiredException)
            {
                _ret = _ret.ItemExpiredException(ex,ex.Message);
                context.Response.StatusCode = (int)_ret.StatusCode;
                
                await context.Response.WriteAsync(JsonConvert.SerializeObject(_ret, settings));
                return;
            }

            if (ex is EntityNotFoundException)
            {
                _ret = _ret.EntityNotFound(ex, ex.Message);
                context.Response.StatusCode = (int)_ret.StatusCode;
                
                await context.Response.WriteAsync(JsonConvert.SerializeObject(_ret, settings));
                return;
            }

            var retGeneric = _ret.GenericError(ex, ErrorCodeEnum.GENERIC_ERROR, ex.Message);

            context.Response.StatusCode = (int)retGeneric.StatusCode;
            await context.Response.WriteAsync(JsonConvert.SerializeObject(retGeneric, settings));
            return;

            // Se não for uma exceção conhecida, você pode lidar com ela de outra forma ou deixar que seja propagada.
            throw;
        }
    }
}
