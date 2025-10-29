using Cime.BuildingBlocks.GlobalModels;
using Microsoft.AspNetCore.Http;

namespace Cime.BuildingBlocks.GlobalExtensions
{
    public static class ReturnExtensions
    {
        public static ReturnDto<T> Created<T>(this T obj)
        {
            var ret = new ReturnDto<T>
            {
                Data = obj,
                StatusCode = StatusCodes.Status201Created,
                Message = "Created"
            };

            return ret;
        }

        public static ReturnDto<T> Ok<T>(this T obj)
        {
            var ret = new ReturnDto<T>
            {
                Data = obj,
                StatusCode = StatusCodes.Status200OK,
                Message = "Success"
            };

            return ret;
        }

        public static ReturnDto<T> Updated<T>(this T obj)
        {
            var ret = new ReturnDto<T>
            {
                Data = obj,
                StatusCode = StatusCodes.Status200OK,
                Message = "Updated"
            };

            return ret;
        }

        public static ReturnDto<T> NoContent<T>(this object obj)
        {
            var ret = new ReturnDto<T>
            {
                StatusCode = StatusCodes.Status204NoContent,
                Message = "No Content"
            };

            return ret;
        }

        public static ReturnDto<T> UnprocessableEntity<T>(this T obj, string message = null)
        {
            var ret = new ReturnDto<T>
            {
                Data = obj,
                StatusCode = StatusCodes.Status422UnprocessableEntity,
                Message = string.IsNullOrEmpty(message) ? "" : ""
            };

            return ret;
        }
    }
}