
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Http;


namespace Cime.BuildingBlocks.GlobalModels
{

    public class Error
    {
        public ErrorCodeEnum Code { get; set; }
        public string Description { get; set; }
        public string Message { get; set; }
        public string? StackTrace { get; set; }
        public object Data { get; set; }


    }


    public class ReturnDto<T>
    {
        public readonly VersionBase _version;

        [JsonIgnore]
        public int StatusCode { get; set; }


        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string Message { get; set; }


        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public T Data { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Error Error { get; set; }

        public VersionBase Version { get; set; } = new VersionBase
        {
            DateRelease = DateOnly.Parse("2024-11-01"),
            Release = "ALHPA.0.0.1",
            VersionAPI = "1.0",
            TimeZone = "UTC"
        };


        public ReturnDto<Error> GenericError(Exception? err = null, ErrorCodeEnum errorCode = ErrorCodeEnum.GENERIC_ERROR, string message = "")
        {
            return new ReturnDto<Error>
            {
                Error = new Error
                {
                    Message = message == "" ? "Error while trying to perform the action." : message,
                    Code = errorCode,
                    Description = Enum.GetName(typeof(ErrorCodeEnum), errorCode)!,
                    StackTrace = err.StackTrace ?? null,
                    Data = err ?? null,

                },
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }

        public ReturnDto<Error> DatabaseError(Exception err = null, ErrorCodeEnum errorCode = ErrorCodeEnum.DATABASE_ERROR, string message = "")
        {
            return new ReturnDto<Error>
            {
                Error = new Error
                {
                    Message = message == "" ? "Error while trying to perform the action." : message,
                    Code = errorCode,
                    Description = Enum.GetName(typeof(ErrorCodeEnum), errorCode),
                    //StackTrace = err.StackTrace ?? null,
                    Data = err ?? null,
                },
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }

        public ReturnDto<Error> HmecNotValid(ErrorCodeEnum errorCode = ErrorCodeEnum.INVALID_HMEC, string message = "")
        {
            return new ReturnDto<Error>
            {
                Error = new Error
                {
                    Message = message == "" ? "The informed hmec is invalid." : message,
                    Code = errorCode,
                    Description = Enum.GetName(typeof(ErrorCodeEnum), errorCode),
                    Data = null
                },
                StatusCode = StatusCodes.Status401Unauthorized
            };
        }

        public ReturnDto<Error> EntityNotFound(Exception err = null, string message = "")
        {
            return new ReturnDto<Error>
            {
                Error = new Error
                {
                    Message = message == "" ? "Error while trying to perform the action." : message,
                    Code = ErrorCodeEnum.ENTITY_NOT_FOUND,
                    Description = Enum.GetName(typeof(ErrorCodeEnum), ErrorCodeEnum.ENTITY_NOT_FOUND),
                    //StackTrace = err.StackTrace ?? null,
                    Data = err ?? null,
                },
                StatusCode = StatusCodes.Status422UnprocessableEntity
            };
        }

        public ReturnDto<Error> ItemExpiredException(Exception err = null, string message = "", ErrorCodeEnum errorCodeEnum = ErrorCodeEnum.ITEM_EXPIRED)
        {
            return new ReturnDto<Error>
            {
                Error = new Error
                {
                    Message = message == "" ? "Error while trying to perform the action." : message,
                    Code = ErrorCodeEnum.ITEM_EXPIRED,
                    Description = Enum.GetName(typeof(ErrorCodeEnum), ErrorCodeEnum.ITEM_EXPIRED),
                    //StackTrace = err.StackTrace ?? null,
                    Data = err ?? null,
                },
                StatusCode = StatusCodes.Status410Gone
            };
        }



    }
}