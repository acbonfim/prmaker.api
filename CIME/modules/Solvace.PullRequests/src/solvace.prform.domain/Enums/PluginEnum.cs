namespace solvace.prform.domain.Enums;

public enum PluginEnum
{
    AI = 3,
    AZURE_DEVOPS = 8,
	PULLREQUEST = 9,
    BUCKET_IMAGE = 11,

}

public static class EPlugin
{
    public static int AI => (int)PluginEnum.AI;
    public static int AZURE_DEVOPS => (int)PluginEnum.AZURE_DEVOPS;
    public static int PULLREQUEST => (int)PluginEnum.PULLREQUEST;
    public static int BUCKET_IMAGE => (int)PluginEnum.BUCKET_IMAGE;
}