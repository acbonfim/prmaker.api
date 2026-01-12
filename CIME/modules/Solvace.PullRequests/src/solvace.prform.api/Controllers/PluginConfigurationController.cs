using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using solvace.prform.application;
using solvace.prform.application.Contracts;
using solvace.prform.domain.Extensions;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.prform.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize(Policy = "admin")]
public class PluginConfigurationController : ControllerBase
{
    private readonly IPluginApplication _application;
    private readonly IPluginCacheManager _pluginCacheManager;

    public PluginConfigurationController(IPluginApplication application, IPluginCacheManager pluginCacheManager)
    {
        _application = application;
        _pluginCacheManager = pluginCacheManager;
    }
    
    [HttpPost]
    public async Task<ActionResult<PluginRespose>> Create(PluginRequest request, CancellationToken cancellationToken)
    {
        var created = await _application.Create(request, cancellationToken);
        
        return Ok(created);
    }
    
    [HttpDelete("delete/{pluginId:int}")]
    public async Task<ActionResult<PluginRespose>> Delete([FromRoute] int pluginId, CancellationToken cancellationToken)
    {
        await _application.DeletePlugin(pluginId, cancellationToken);
        return Ok();
    }
    
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
        
        IDictionary<string, string> dictionary = new Dictionary<string, string>();
        
        if(!string.IsNullOrEmpty(plugin.Configurations.Options))
            dictionary = plugin.Configurations.Options.JsonToListOfDictionaries()[0];

        return Ok(new PluginRespose()
            {
                Id = plugin.Id,
                Configurations = dictionary,
                Description = plugin.Description
            }
        );
    }
    
    [HttpPut("update-configuration/{pluginId:int}")]
    public async Task<ActionResult<PluginRespose>> UpdateConfiguration([FromRoute] int pluginId,[FromBody] PluginRequest request, CancellationToken cancellationToken)
    {
        var user = await _application.UpdateConfiguration(pluginId, request, cancellationToken);

        return Ok(user);
    }
}