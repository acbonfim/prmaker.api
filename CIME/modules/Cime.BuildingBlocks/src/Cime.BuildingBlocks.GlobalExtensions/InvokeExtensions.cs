using System.Reflection;
using Microsoft.AspNetCore.Http;
using Cliqx.BuildingBlocks.GlobalModels;

namespace Cliqx.BuildingBlocks.GlobalExtensions;

public static class InvokeExtensions
{
    public static async Task<Retorno> InvokeIntegration(this IHttpContextAccessor _accessor, string assemblyName)
    {
        var query = _accessor.HttpContext.Request.Query;

        if (!query.TryGetValue("className", out var className))
        {
            throw new Exception("The class name is already defined.");
        }

        if (!query.TryGetValue("methodName", out var methodName))
        {
            throw new Exception("The method name is already defined.");
        }

        if (!query.TryGetValue("chatUUID", out var chatUUID))
        {
            throw new Exception("The method name is already defined.");
        }

        var cancellationToken = _accessor.HttpContext.RequestAborted;

        var nameclass = $"{assemblyName}." + className;
        var assembly = Assembly.Load(assemblyName);
        Type classType = assembly.GetType(nameclass);

        if (classType is null)
            throw new ItemNotFoundException($"Classe '{className}' não encontrada");


        var constructor = classType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[]
            {
                typeof(IServiceProvider),
                typeof(string)
            }, null);

        if (constructor == null)
            throw new Exception($"Constructor não encontrado para a classe do plugin");

        var serviceProvider = _accessor.HttpContext.RequestServices;


        var parameters = new object[]
        {
                serviceProvider,
                chatUUID.First()!.ToString()
        };


        object instance = constructor.Invoke(parameters);

        MethodInfo methodInfo = classType.GetMethod(methodName);

        var methodParams = new object[]
        {
                cancellationToken
        };

        if (methodInfo != null)
        {
            try
            {
                var result = await (Task<Retorno>)methodInfo.Invoke(instance, methodParams);

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidPropertyException($"Erro ao chamar o método: {ex.Message}");
            }
        }
        else
        {
            throw new ItemNotFoundException($"Método '{methodName}' não encontrado na classe '{className}'");
        }

    }
}