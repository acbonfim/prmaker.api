using System.ComponentModel;

namespace Cliqx.BuildingBlocks.GlobalModels
{
    public enum ErrorCodeEnum
    {
        [Description("An error occurred while trying to perform the action.")]
        GENERIC_ERROR,

        METHOD_NOT_IMPLEMENTED,
        [Description("The requested item was not found.")]
        ITEM_NOT_FOUND,
        [Description("An error occurred while sending an email.")]
        ERROR_SEND_EMAIL,
        [Description("A required parameter was not provided.")]
        PARAMETER_NOT_INFORMED,
        [Description("A parameter is null or blank.")]
        PARAMETER_IS_NULL_OR_BLANK,

        [Description("Property not matching with model")]
        INVALID_PROPERTY,

        [Description("Error on database. See inner exception for more details.")]
        DATABASE_ERROR,

        [Description("Error on database. The entity was not found with the specified parameters.")]
        ENTITY_NOT_FOUND,
        [Description("The item has expired.")]
        ITEM_EXPIRED,
        [Description("The HMEC is not valid.")]
        INVALID_HMEC,
    }
}
