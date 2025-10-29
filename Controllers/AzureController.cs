using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using solvace.prform.Data.Requests;

namespace solvace.prform.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AzureController : ControllerBase
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public AzureController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient();
        _configuration = configuration;
    }

    /// <summary>
    /// Testa a configuração do Azure DevOps
    /// </summary>
    /// <returns>Status da configuração</returns>
    [HttpGet("test")]
    public IActionResult TestConfiguration()
    {
        try
        {
            var org = _configuration["AZURE_ORG"];
            var project = _configuration["AZURE_PROJECT"];
            var pat = _configuration["AZURE_PAT"];

            var baseUrl = GetAzureBaseUrl();

            return Ok(new
            {
                organization = org,
                project = project,
                hasToken = !string.IsNullOrEmpty(pat),
                tokenLength = pat?.Length ?? 0,
                baseUrl = baseUrl,
                message = "Configuração carregada com sucesso"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Consulta um card/work item pelo ID
    /// </summary>
    /// <param name="id">ID do card no Azure DevOps</param>
    /// <returns>Dados do card</returns>
    [HttpGet("card/{id}")]
    public async Task<IActionResult> GetCard(string id)
    {
        try
        {
            var baseUrl = GetAzureBaseUrl();
            var apiVersion = "7.0";
            var url = $"{baseUrl}/wit/workitems/{id}?api-version={apiVersion}";

            Console.WriteLine($"Tentando acessar: {url}");

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAzureAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Response: {content}");

            if (response.IsSuccessStatusCode)
            {
                return Content(content, "application/json");
            }

            return StatusCode((int)response.StatusCode, new
            {
                error = $"Erro {response.StatusCode}",
                details = content,
                url = url
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao consultar o card: {ex.Message}");
            return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    /// <summary>
    /// Atualiza horas de um card
    /// </summary>
    /// <param name="id">ID do card</param>
    /// <param name="request">Dados das horas</param>
    /// <returns>Resultado da atualização</returns>
    [HttpPatch("card/{id}/hours")]
    public async Task<IActionResult> UpdateHours(string id, [FromBody] UpdateHoursRequest request)
    {
        try
        {
            var fieldMap = new Dictionary<string, string>
            {
                { "CompletedWork", "Microsoft.VSTS.Scheduling.CompletedWork" },
                { "OriginalEstimate", "Microsoft.VSTS.Scheduling.OriginalEstimate" },
                { "RemainingWork", "Microsoft.VSTS.Scheduling.RemainingWork" }
            };

            if (!fieldMap.TryGetValue(request.TypeHours, out var fieldPath))
            {
                return BadRequest(new { error = "Tipo de hora inválido." });
            }

            var baseUrl = GetAzureBaseUrl();
            var apiVersion = "7.0";
            var url = $"{baseUrl}/wit/workitems/{id}?api-version={apiVersion}";

            var patchData = new[]
            {
                new { op = "add", path = $"/fields/{fieldPath}", value = request.Hours }
            };

            var json = JsonSerializer.Serialize(patchData);
            var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

            var requestMessage = new HttpRequestMessage(HttpMethod.Patch, url)
            {
                Content = content
            };
            AddAzureAuthHeader(requestMessage);

            var response = await _httpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return Content(responseContent, "application/json");
            }

            return StatusCode((int)response.StatusCode, responseContent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao atualizar horas: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Atualiza status de um card
    /// </summary>
    /// <param name="id">ID do card</param>
    /// <param name="request">Novo status</param>
    /// <returns>Resultado da atualização</returns>
    [HttpPatch("card/{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] UpdateStatusRequest request)
    {
        try
        {
            var baseUrl = GetAzureBaseUrl();
            var apiVersion = "7.0";
            var url = $"{baseUrl}/wit/workitems/{id}?api-version={apiVersion}";

            var patchData = new[]
            {
                new { op = "add", path = "/fields/System.State", value = request.Status }
            };

            var json = JsonSerializer.Serialize(patchData);
            var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

            var requestMessage = new HttpRequestMessage(HttpMethod.Patch, url)
            {
                Content = content
            };
            AddAzureAuthHeader(requestMessage);

            var response = await _httpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return Content(responseContent, "application/json");
            }

            return StatusCode((int)response.StatusCode, responseContent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao alterar status: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }


    /// <summary>
    /// Atualiza Root Cause de um card - aceita JSON, Markdown, HTML ou texto simples
    /// </summary>
    /// <param name="id">ID do card</param>
    /// <returns>Resultado da atualização</returns>
    [HttpPost("card/{id}/rootcause")]
    [Consumes("application/json", "text/plain", "text/html", "text/markdown")]
    public async Task<IActionResult> UpdateRootCause(string id)
    {
        try
        {
            // Ler o body diretamente
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            var requestBody = await reader.ReadToEndAsync();

            string rootCauseText = requestBody ?? "";

            // Verificar se é JSON válido
            if (!string.IsNullOrEmpty(rootCauseText) && rootCauseText.TrimStart().StartsWith("{"))
            {
                try
                {
                    var jsonRequest = JsonSerializer.Deserialize<UpdateRootCauseRequest>(rootCauseText);
                    rootCauseText = jsonRequest?.RootCause ?? rootCauseText;
                }
                catch
                {
                    // Se falhar na deserialização, usar o texto direto
                    rootCauseText = requestBody ?? "";
                }
            }
            // Se não for JSON, usar o texto direto (Markdown, HTML, texto simples)

            var baseUrl = GetAzureBaseUrl();
            var apiVersion = "7.0";
            var url = $"{baseUrl}/wit/workitems/{id}?api-version={apiVersion}";

            // Configurar opções de serialização para tratar caracteres especiais
            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false
            };

            var patchData = new[]
            {
                new { op = "add", path = "/fields/Custom.RCATechnicalCategorytext", value = rootCauseText }
            };

            var json = JsonSerializer.Serialize(patchData, jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

            var requestMessage = new HttpRequestMessage(HttpMethod.Patch, url)
            {
                Content = content
            };
            AddAzureAuthHeader(requestMessage);

            var response = await _httpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return Content(responseContent, "application/json");
            }

            return StatusCode((int)response.StatusCode, responseContent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao atualizar Root Cause: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }



    /// <summary>
    /// Anexa arquivo a um card - aceita upload de arquivo real
    /// </summary>
    /// <param name="id">ID do card</param>
    /// <param name="file">Arquivo para anexar</param>
    /// <returns>Resultado do anexo</returns>
    [HttpPost("card/{id}/attachment")]
    public async Task<IActionResult> AttachFile(string id, IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "Nenhum arquivo fornecido" });
            }

            var baseUrl = GetAzureBaseUrl();
            var apiVersion = "7.0";

            // Upload do arquivo
            var uploadUrl = $"{baseUrl}/wit/attachments?fileName={Uri.EscapeDataString(file.FileName)}&api-version={apiVersion}";

            using var fileStream = file.OpenReadStream();
            var uploadContent = new StreamContent(fileStream);
            // Azure DevOps aceita apenas application/octet-stream para uploads
            uploadContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl)
            {
                Content = uploadContent
            };
            AddAzureAuthHeader(uploadRequest);

            var uploadResponse = await _httpClient.SendAsync(uploadRequest);
            var uploadResponseContent = await uploadResponse.Content.ReadAsStringAsync();

            if (!uploadResponse.IsSuccessStatusCode)
            {
                return StatusCode((int)uploadResponse.StatusCode, uploadResponseContent);
            }

            var uploadResult = JsonSerializer.Deserialize<JsonElement>(uploadResponseContent);
            var attachmentUrl = uploadResult.GetProperty("url").GetString();

            // Vincular arquivo ao card
            var linkUrl = $"{baseUrl}/wit/workitems/{id}?api-version={apiVersion}";
            var linkData = new[]
            {
                new
                {
                    op = "add",
                    path = "/relations/-",
                    value = new
                    {
                        rel = "AttachedFile",
                        url = attachmentUrl,
                        attributes = new { comment = "Arquivo anexado via API personalizada" }
                    }
                }
            };

            var linkJson = JsonSerializer.Serialize(linkData);
            var linkContent = new StringContent(linkJson, Encoding.UTF8, "application/json-patch+json");

            var linkRequest = new HttpRequestMessage(HttpMethod.Patch, linkUrl)
            {
                Content = linkContent
            };
            AddAzureAuthHeader(linkRequest);

            var linkResponse = await _httpClient.SendAsync(linkRequest);
            var linkResponseContent = await linkResponse.Content.ReadAsStringAsync();

            if (linkResponse.IsSuccessStatusCode)
            {
                return Ok(new { mensagem = "Arquivo anexado com sucesso!", result = linkResponseContent });
            }

            return StatusCode((int)linkResponse.StatusCode, linkResponseContent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao anexar arquivo: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Busca cards por status usando WIQL
    /// </summary>
    /// <param name="status">Status para filtrar (ex: "New", "Active", "Resolved")</param>
    /// <param name="workItemType">Tipo do work item (opcional, ex: "Bug", "Task", "User Story")</param>
    /// <param name="maxResults">Número máximo de resultados (padrão: 50)</param>
    /// <returns>Lista de cards com o status especificado</returns>
    [HttpGet("cards/by-status")]
    public async Task<IActionResult> GetCardsByStatus(
        [FromQuery] string status,
        [FromQuery] string? workItemType = null,
        [FromQuery] int maxResults = 50)
    {
        try
        {
            var baseUrl = GetAzureBaseUrl();
            var apiVersion = "7.0";
            var url = $"{baseUrl}/wit/wiql?api-version={apiVersion}";

            // Construir query WIQL
            var wiqlQuery = $@"
                SELECT [System.Id], [System.Title], [System.State], [System.WorkItemType], [System.AssignedTo], [System.CreatedDate]
                FROM WorkItems
                WHERE [System.State] = '{status}'";

            if (!string.IsNullOrEmpty(workItemType))
            {
                wiqlQuery += $" AND [System.WorkItemType] = '{workItemType}'";
            }

            wiqlQuery += $" ORDER BY [System.CreatedDate] DESC";

            var queryData = new
            {
                query = wiqlQuery
            };

            var json = JsonSerializer.Serialize(queryData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };
            AddAzureAuthHeader(request);

            Console.WriteLine($"Executando query WIQL: {wiqlQuery}");

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Response: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                var queryResult = JsonSerializer.Deserialize<JsonElement>(responseContent);

                // Se a query retornou IDs, buscar detalhes dos work items
                if (queryResult.TryGetProperty("workItems", out var workItems) && workItems.GetArrayLength() > 0)
                {
                    var ids = workItems.EnumerateArray()
                        .Select(item => item.GetProperty("id").GetInt32())
                        .Take(maxResults)
                        .ToArray();

                    if (ids.Length > 0)
                    {
                        // Buscar detalhes dos work items
                        var idsString = string.Join(",", ids);
                        var detailsUrl = $"{baseUrl}/wit/workitems?ids={idsString}&api-version={apiVersion}";

                        var detailsRequest = new HttpRequestMessage(HttpMethod.Get, detailsUrl);
                        AddAzureAuthHeader(detailsRequest);

                        var detailsResponse = await _httpClient.SendAsync(detailsRequest);
                        var detailsContent = await detailsResponse.Content.ReadAsStringAsync();

                        if (detailsResponse.IsSuccessStatusCode)
                        {
                            return Content(detailsContent, "application/json");
                        }
                    }
                }

                return Ok(new { message = "Nenhum work item encontrado com o status especificado", query = wiqlQuery });
            }

            return StatusCode((int)response.StatusCode, new
            {
                error = $"Erro {response.StatusCode}",
                details = responseContent,
                query = wiqlQuery
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao buscar cards por status: {ex.Message}");
            return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    /// <summary>
    /// Busca cards por múltiplos status
    /// </summary>
    /// <param name="statuses">Lista de status separados por vírgula (ex: "New,Active,Resolved")</param>
    /// <param name="workItemType">Tipo do work item (opcional)</param>
    /// <param name="maxResults">Número máximo de resultados (padrão: 50)</param>
    /// <returns>Lista de cards com os status especificados</returns>
    [HttpGet("cards/by-statuses")]
    public async Task<IActionResult> GetCardsByStatuses(
        [FromQuery] string statuses,
        [FromQuery] string? workItemType = null,
        [FromQuery] int maxResults = 50)
    {
        try
        {
            var statusList = statuses.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToArray();

            if (statusList.Length == 0)
            {
                return BadRequest("Pelo menos um status deve ser fornecido");
            }

            var baseUrl = GetAzureBaseUrl();
            var apiVersion = "7.0";
            var url = $"{baseUrl}/wit/wiql?api-version={apiVersion}";

            // Construir query WIQL com múltiplos status
            var statusConditions = string.Join(" OR ", statusList.Select(s => $"[System.State] = '{s}'"));

            var wiqlQuery = $@"
                SELECT [System.Id], [System.Title], [System.State], [System.WorkItemType], [System.AssignedTo], [System.CreatedDate]
                FROM WorkItems
                WHERE ({statusConditions})";

            if (!string.IsNullOrEmpty(workItemType))
            {
                wiqlQuery += $" AND [System.WorkItemType] = '{workItemType}'";
            }

            wiqlQuery += $" ORDER BY [System.CreatedDate] DESC";

            var queryData = new
            {
                query = wiqlQuery
            };

            var json = JsonSerializer.Serialize(queryData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };
            AddAzureAuthHeader(request);

            Console.WriteLine($"Executando query WIQL: {wiqlQuery}");

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var queryResult = JsonSerializer.Deserialize<JsonElement>(responseContent);

                if (queryResult.TryGetProperty("workItems", out var workItems) && workItems.GetArrayLength() > 0)
                {
                    var ids = workItems.EnumerateArray()
                        .Select(item => item.GetProperty("id").GetInt32())
                        .Take(maxResults)
                        .ToArray();

                    if (ids.Length > 0)
                    {
                        var idsString = string.Join(",", ids);
                        var detailsUrl = $"{baseUrl}/wit/workitems?ids={idsString}&api-version={apiVersion}";

                        var detailsRequest = new HttpRequestMessage(HttpMethod.Get, detailsUrl);
                        AddAzureAuthHeader(detailsRequest);

                        var detailsResponse = await _httpClient.SendAsync(detailsRequest);
                        var detailsContent = await detailsResponse.Content.ReadAsStringAsync();

                        if (detailsResponse.IsSuccessStatusCode)
                        {
                            return Content(detailsContent, "application/json");
                        }
                    }
                }

                return Ok(new { message = "Nenhum work item encontrado com os status especificados", query = wiqlQuery });
            }

            return StatusCode((int)response.StatusCode, new
            {
                error = $"Erro {response.StatusCode}",
                details = responseContent,
                query = wiqlQuery
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao buscar cards por status: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Lista todos os estados disponíveis no projeto
    /// </summary>
    /// <returns>Lista de todos os estados possíveis</returns>
    [HttpGet("states/all")]
    public async Task<IActionResult> GetAllStates()
    {
        try
        {
            var baseUrl = GetAzureBaseUrl();
            var apiVersion = "7.0";
            var url = $"{baseUrl}/wit/workitemtypes?api-version={apiVersion}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAzureAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<JsonElement>(content);
                var allStates = new HashSet<string>();

                if (result.TryGetProperty("value", out var workItemTypes))
                {
                    foreach (var workItemType in workItemTypes.EnumerateArray())
                    {
                        if (workItemType.TryGetProperty("states", out var states))
                        {
                            foreach (var state in states.EnumerateArray())
                            {
                                if (state.TryGetProperty("name", out var stateName))
                                {
                                    allStates.Add(stateName.GetString() ?? "");
                                }
                            }
                        }
                    }
                }

                return Ok(new
                {
                    states = allStates.OrderBy(s => s).ToArray(),
                    count = allStates.Count
                });
            }

            return StatusCode((int)response.StatusCode, content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter todos os estados: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Lista os tipos de horas disponíveis para work items
    /// </summary>
    /// <returns>Lista de tipos de horas com suas descrições</returns>
    [HttpGet("hours/types")]
    public IActionResult GetHoursTypes()
    {
        try
        {
            var hoursTypes = new[]
            {
                new
                {
                    type = "CompletedWork",
                    name = "Trabalho Concluído",
                    description = "Horas já trabalhadas no item",
                    fieldPath = "Microsoft.VSTS.Scheduling.CompletedWork"
                },
                new
                {
                    type = "OriginalEstimate",
                    name = "Estimativa Original",
                    description = "Estimativa inicial de horas para o item",
                    fieldPath = "Microsoft.VSTS.Scheduling.OriginalEstimate"
                },
                new
                {
                    type = "RemainingWork",
                    name = "Trabalho Restante",
                    description = "Horas restantes para completar o item",
                    fieldPath = "Microsoft.VSTS.Scheduling.RemainingWork"
                }
            };

            return Ok(new
            {
                hoursTypes = hoursTypes,
                count = hoursTypes.Length,
                message = "Tipos de horas carregados com sucesso"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter tipos de horas: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Lista apenas os nomes dos tipos de work items disponíveis no projeto
    /// </summary>
    /// <returns>Lista simples com nomes dos tipos de work items</returns>
    [HttpGet("workitem-types")]
    public async Task<IActionResult> GetWorkItemTypes()
    {
        try
        {
            var baseUrl = GetAzureBaseUrl();
            var apiVersion = "7.0";
            var url = $"{baseUrl}/wit/workitemtypes?api-version={apiVersion}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAzureAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<JsonElement>(content);

                if (result.TryGetProperty("value", out var workItemTypes))
                {
                    // Extrair apenas os nomes dos tipos, de forma simples
                    var typeNames = new List<string>();

                    foreach (var type in workItemTypes.EnumerateArray())
                    {
                        if (type.TryGetProperty("name", out var nameElement) &&
                            nameElement.ValueKind == JsonValueKind.String)
                        {
                            var typeName = nameElement.GetString();
                            if (!string.IsNullOrEmpty(typeName))
                            {
                                typeNames.Add(typeName);
                            }
                        }
                    }

                    var sortedTypes = typeNames.OrderBy(t => t).ToArray();

                    return Ok(new
                    {
                        workItemTypes = sortedTypes,
                        count = sortedTypes.Length,
                        message = "Tipos de work items carregados com sucesso"
                    });
                }

                return Ok(new
                {
                    workItemTypes = new string[0],
                    count = 0,
                    message = "Nenhum tipo de work item encontrado"
                });
            }

            return StatusCode((int)response.StatusCode, new
            {
                error = $"Erro {response.StatusCode}",
                details = content
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter tipos de work items: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Lista estados válidos do card
    /// </summary>
    /// <param name="type">Tipo do card</param>
    /// <returns>Lista de estados</returns>
    [HttpGet("card/states/{type}")]
    public async Task<IActionResult> GetCardStates(string type)
    {
        try
        {
            var baseUrl = GetAzureBaseUrl();
            var apiVersion = "7.0";
            var url = $"{baseUrl}/wit/workitemtypes/{type}/fields/System.State?api-version={apiVersion}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAzureAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<JsonElement>(content);
                if (result.TryGetProperty("allowedValues", out var allowedValues))
                {
                    return Ok(allowedValues);
                }
                return Ok(new object[0]);
            }

            return StatusCode((int)response.StatusCode, content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter estados: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private string GetAzureBaseUrl()
    {
        var org = _configuration["AZURE_ORG"];
        var project = _configuration["AZURE_PROJECT"];

        if (string.IsNullOrEmpty(org) || string.IsNullOrEmpty(project))
            throw new InvalidOperationException("Configurações do Azure DevOps não encontradas");

        // URL encode do nome do projeto para lidar com espaços e caracteres especiais
        var encodedProject = Uri.EscapeDataString(project);
        return $"https://dev.azure.com/{org}/{encodedProject}/_apis";
    }

    private void AddAzureAuthHeader(HttpRequestMessage request)
    {
        var pat = _configuration["AZURE_PAT"];
        if (string.IsNullOrEmpty(pat))
            throw new InvalidOperationException("AZURE_PAT não configurado");

        // Azure DevOps usa Basic Auth com PAT como senha e usuário vazio
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);

        // Headers adicionais necessários para Azure DevOps
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("User-Agent", "SolvacePRForm/1.0");
    }
}
