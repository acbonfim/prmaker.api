using cliqx.auth.api.Dtos;

namespace cliqx.auth.api.Extensions
{
    public static class RetornoDtoExtensions
    {
        public static RetornoDto AsRetornoDto(this object obj, string message = "", bool success = false) =>
            new RetornoDto
            {
                Message = message,
                Success = success,
                Object = obj
            };
    }
}