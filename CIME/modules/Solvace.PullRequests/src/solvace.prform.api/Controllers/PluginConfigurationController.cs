using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using solvace.prform.application;
using solvace.prform.application.Contracts;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Enums;
using solvace.prform.domain.Extensions;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.prform.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize(Roles = "admin, user, support")]
public class PluginConfigurationController : ControllerBase
{
    private readonly IPluginApplication _application;
    private readonly IPluginCacheManager _pluginCacheManager;
    private readonly IUserPluginConfigurationApplication _userConfigurations;
    private readonly IPluginConfigurationResolver _resolver;

    public PluginConfigurationController(IPluginApplication application, IPluginCacheManager pluginCacheManager,
        IUserPluginConfigurationApplication userConfigurations, IPluginConfigurationResolver resolver)
    {
        _application = application;
        _pluginCacheManager = pluginCacheManager;
        _userConfigurations = userConfigurations;
        _resolver = resolver;
    }
    
    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<ActionResult<PluginRespose>> Create(PluginRequest request, CancellationToken cancellationToken)
    {
        var created = await _application.Create(request, cancellationToken);
        
        return Ok(created);
    }
    
    [Authorize(Roles = "admin")]
    [HttpDelete("delete/{pluginId:int}")]
    public async Task<ActionResult<PluginRespose>> Delete([FromRoute] int pluginId, CancellationToken cancellationToken)
    {
        await _application.DeletePlugin(pluginId, cancellationToken);
        return Ok();
    }
    
    [Authorize(Roles = "admin")]
    [HttpGet("get-all")]
    public async Task<ActionResult<PluginRespose>> Get(CancellationToken cancellationToken)
    {
        var plugins = _pluginCacheManager.GetCachedPlugins();
        
        if (plugins == null || !plugins.Any())
        {
            await _pluginCacheManager.RefreshPluginsAsync(cancellationToken);
            plugins = _pluginCacheManager.GetCachedPlugins();
        }
        
        var pluginsResponse = new List<PluginRespose>();

        foreach (var plugin in plugins)
        {
            var pluginResponse = new PluginRespose();
            pluginResponse.Id = plugin.Id;
            pluginResponse.Description = plugin.Description;
            pluginResponse.AdminOnly = plugin.AdminOnly;
            pluginResponse.IsPersonal = plugin.IsPersonal;
            pluginResponse.PersonalFields = plugin.GetPersonalFieldKeys();
            pluginResponse.IsOptional = plugin.IsOptional;
            pluginResponse.FieldSettings = plugin.GetFieldSettings();

            if(!string.IsNullOrEmpty(plugin.Configurations.Options))
                pluginResponse.Configurations = plugin.Configurations.Options.JsonToListOfDictionaries()[0];
            pluginsResponse.Add(pluginResponse);
        }
        return Ok(pluginsResponse);
    }
    
    [HttpGet("get-all-by-id")]
    public async Task<ActionResult<PluginRespose>> GetPluginById([FromQuery]int id, CancellationToken cancellationToken)
    {
        var plugin = _pluginCacheManager.GetCachedPluginById(id);

        if (plugin == null)
        {
            plugin = await _application.GetPluginById(id, cancellationToken);
        }

        // Regra por plugin: AdminOnly => somente admin; caso contrário, qualquer usuário logado.
        if (plugin.AdminOnly)
        {
            var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
            if (!roles.Contains("admin"))
                return Forbid();
        }

        IDictionary<string, string> dictionary = new Dictionary<string, string>();

        if (plugin.IsPersonal)
        {
            // Uso pessoal (D8): devolve os valores DO USUÁRIO, com segredos mascarados — nunca o global.
            dictionary = await PersonalValuesAsync(plugin.Id, cancellationToken);
        }
        else if(!string.IsNullOrEmpty(plugin.Configurations.Options))
            dictionary = plugin.Configurations.Options.JsonToListOfDictionaries()[0];

        return Ok(new PluginRespose()
            {
                Id = plugin.Id,
                Configurations = dictionary,
                Description = plugin.Description,
                AdminOnly = plugin.AdminOnly,
                IsPersonal = plugin.IsPersonal,
                PersonalFields = plugin.GetPersonalFieldKeys(),
                IsOptional = plugin.IsOptional,
                FieldSettings = plugin.GetFieldSettings()
            }
        );
    }
    
    [Authorize(Roles = "admin")]
    [HttpPut("update-configuration/{pluginId:int}")]
    public async Task<ActionResult<PluginRespose>> UpdateConfiguration([FromRoute] int pluginId,[FromBody] PluginRequest request, CancellationToken cancellationToken)
    {
        var user = await _application.UpdateConfiguration(pluginId, request, cancellationToken);

        return Ok(user);
    }
    

    private const string SecretMask = "********";

    private async Task<IDictionary<string, string>> PersonalValuesAsync(int pluginId, CancellationToken cancellationToken)
    {
        if (_resolver.CurrentUserExternalId is not { } user)
            return new Dictionary<string, string>();

        var item = (await _userConfigurations.ListAsync(user, cancellationToken)).FirstOrDefault(x => x.PluginId == pluginId);
        if (item is null)
            return new Dictionary<string, string>();

        // Campos do usuário: valor dele (ou o global, quando o campo usa o padrão — 0011); fixos:
        // valor global. Segredos sempre mascarados.
        return item.Fields.ToDictionary(
            f => f.Key,
            f => f.Sensitive
                ? (f.HasValue ? SecretMask : string.Empty)
                : (f.HasValue || f.UsesGlobalDefault ? f.Value ?? string.Empty : string.Empty));
    }
}