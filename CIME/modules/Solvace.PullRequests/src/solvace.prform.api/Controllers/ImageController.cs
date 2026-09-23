using System.Linq;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.prform.application;
using solvace.prform.application.Contracts;
using solvace.prform.domain.Enums;
using solvace.prform.domain.Extensions;

namespace solvace.prform.Controllers;

public class UploadImageRequest
{
    // Data URL (data:image/png;base64,....) ou base64 puro.
    public string Image { get; set; }
}

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class ImageController : ControllerBase
{
    private readonly IPluginApplication _pluginApplication;
    private readonly IPluginCacheManager _pluginCacheManager;

    public ImageController(IPluginApplication pluginApplication, IPluginCacheManager pluginCacheManager)
    {
        _pluginApplication = pluginApplication;
        _pluginCacheManager = pluginCacheManager;
    }

    /// <summary>
    /// Faz upload assinado de uma imagem para o Cloudinary (credenciais no plugin id 11)
    /// e retorna a secure_url. O ApiSecret nunca sai do servidor.
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromBody] UploadImageRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Image))
            return BadRequest(new { message = "Imagem não informada." });

        var plugin = _pluginCacheManager.GetCachedPluginById(EPlugin.BUCKET_IMAGE)
                     ?? await _pluginApplication.GetPluginById(EPlugin.BUCKET_IMAGE, cancellationToken);

        if (plugin?.Configurations == null)
            return StatusCode(StatusCodes.Status400BadRequest, new { message = "Plugin de imagens (id 11) não configurado." });

        var apiKey = plugin.Configurations.GetConfigurationValue("ApiKey");
        var apiSecret = plugin.Configurations.GetConfigurationValue("ApiSecret");
        var cloudinaryUrl = plugin.Configurations.GetConfigurationValue("CloudinaryUrl");
        var cloudName = ExtractCloudName(cloudinaryUrl);

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret) || string.IsNullOrWhiteSpace(cloudName))
            return StatusCode(StatusCodes.Status400BadRequest, new { message = "Configuração do Cloudinary incompleta no plugin id 11." });

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(StripDataUrl(request.Image));
        }
        catch
        {
            return BadRequest(new { message = "Imagem em base64 inválida." });
        }

        var cloudinary = new Cloudinary(new Account(cloudName, apiKey, apiSecret)) { Api = { Secure = true } };

        using var stream = new MemoryStream(bytes);
        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription($"avatar_{Guid.NewGuid():N}", stream),
            Folder = "prform/avatars",
            Overwrite = true,
            Transformation = new Transformation().Width(320).Height(320).Crop("fill").Gravity("face")
        };

        var result = await cloudinary.UploadAsync(uploadParams, cancellationToken);

        if (result.Error != null || result.SecureUrl == null)
            return StatusCode(StatusCodes.Status502BadGateway, new { message = result.Error?.Message ?? "Falha no upload da imagem." });

        return Ok(new { url = result.SecureUrl.ToString(), publicId = result.PublicId });
    }

    private static string StripDataUrl(string image)
    {
        var idx = image.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? image.Substring(idx + "base64,".Length) : image;
    }

    // De "cloudinary://key:secret@zocbrv74" extrai "zocbrv74".
    private static string ExtractCloudName(string cloudinaryUrl)
    {
        if (string.IsNullOrWhiteSpace(cloudinaryUrl))
            return null;
        var afterAt = cloudinaryUrl.Split('@').LastOrDefault();
        return string.IsNullOrWhiteSpace(afterAt) ? null : afterAt.Trim().TrimEnd('/');
    }
}
