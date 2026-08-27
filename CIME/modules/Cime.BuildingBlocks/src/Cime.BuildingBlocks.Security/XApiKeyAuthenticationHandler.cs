using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;

namespace Cime.BuildingBlocks.Security
{
    public class XApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly string _secretKey;
        private readonly string _urlAuth;
        private readonly string[] _servicesAllowed;
        private readonly bool _verifyOnlineUserServices;

        [Obsolete]
        public XApiKeyAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            IConfiguration configuration)
            : base(options, logger, encoder, clock)
        {
            var authSection = configuration.GetSection("Auth");
            _secretKey = authSection["Secret"]!;
            _urlAuth = authSection["UrlBase"]!;
            var servicesAllowed = authSection.GetSection("ServicesAllowed").Get<string[]>();
            _servicesAllowed = servicesAllowed ?? Array.Empty<string>();

            _verifyOnlineUserServices = Convert.ToBoolean(authSection["VerifyOnlineUserServices"]!);
        }

        protected override async Task<Task> HandleChallengeAsync(AuthenticationProperties properties)
        {

            if (Response.StatusCode != 200)
            {
                return Task.CompletedTask;
            }


            await base.HandleForbiddenAsync(properties);
            return Task.CompletedTask;
        }


        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Path.ToString().Equals("/"))
                return AuthenticateResult.NoResult();

            if (Request.Path.ToString().StartsWith("/swagger"))
                return AuthenticateResult.NoResult();

            if(Request.Method == "OPTIONS")
                return AuthenticateResult.NoResult();

            if (!Request.Headers.TryGetValue("x-api-key", out var apiKey))
            {
                Console.WriteLine("Request:");
                Response.StatusCode = 401;
                Response.WriteAsync("Unauthorized: X-API-Key header not found").Wait();
                return AuthenticateResult.Fail("X-API-Key header not found");
            }


            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Response.StatusCode = 401;
                Response.WriteAsync("Unauthorized: Api key Invalid").Wait();
                return AuthenticateResult.Fail("Invalid X-API-Key");
            }

            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_secretKey)),
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false,
                ValidateActor = true
            };

            try
            {
                var principal = tokenHandler.ValidateToken(apiKey, tokenValidationParameters, out _);
                var identity = new ClaimsIdentity(principal.Claims, Scheme.Name);

                var usernameClaim = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);
                var userId = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
                var tokenType = principal.Claims.FirstOrDefault(c => c.Type == "tokenType");

                if (tokenType is null)
                {
                    Response.StatusCode = 401;
                    Response.WriteAsync("Unauthorized: Token type not found").Wait();
                    return AuthenticateResult.Fail("Unauthorized: Token type not found");
                }

                if (tokenType.Value != "x-api-key")
                {
                    Response.StatusCode = 403;
                    Response.WriteAsync($"Forbidden: Token type ({tokenType.Value}) not valid").Wait();
                    return AuthenticateResult.Fail("Forbidden: Token type not valid");
                }

                if (!_verifyOnlineUserServices)
                {
                    var services = principal.Claims.Where(x => x.Type == "Services");

                    if (!await IsUserInService(services))
                    {
                        Response.StatusCode = 403;
                        Response.WriteAsync("Forbidden: User not have access to service").Wait();
                        return AuthenticateResult.Fail("User not have access to service");
                    }
                }
                else
                {
                    if (!await HasAccessToServices(Convert.ToInt32(userId.Value)))
                    {
                        Response.StatusCode = 403;
                        Response.WriteAsync("Forbidden: User not have access to service").Wait();
                        return AuthenticateResult.Fail("User not have access to service");
                    }
                }

                if (usernameClaim != null)
                {
                    var username = usernameClaim.Value;

                    if (!await IsUserActive(username))
                    {
                        Response.StatusCode = 403;
                        Response.WriteAsync("Forbidden: User is inactive").Wait();
                        return AuthenticateResult.Fail("User is inactive");
                    }
                }

                var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

                return AuthenticateResult.Success(ticket);
            }
            catch (SecurityTokenException)
            {
                return AuthenticateResult.Fail("Invalid token");
            }


        }



        private async Task<bool> IsUserInService(IEnumerable<Claim> services)
        {
            if (services.Count() == 0)
                return false;

            if (services.Any(x => _servicesAllowed.Contains(x.Value)))
                return true;

            return false;
        }

        private async Task<bool> HasAccessToServices(int userId)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var urlBase = $"{_urlAuth}/Service/HasAccessToServices?userId={userId}";
                    HttpContent content = new StringContent(JsonConvert.SerializeObject(_servicesAllowed), Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await client.PostAsync(urlBase, content).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = response.Content.ReadAsStringAsync().Result.AsTypedReturn<RetornoDto<object>>();

                        return (bool)responseContent.Object;
                    }
                    else
                    {
                        throw new Exception($"Erro na requisição. Código de status: {response.StatusCode}");
                    }

                }
            }
            catch (System.Exception e)
            {
                throw new Exception("Erro ao processar a requisição.", e);
            }

        }



        private async Task<bool> IsUserActive(string username)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var urlBase = $"{_urlAuth}/user/is-user-active?username={username}";
                    HttpContent content = new StringContent("", Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await client.GetAsync(urlBase).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = response.Content.ReadAsStringAsync().Result.AsTypedReturn<RetornoDto<object>>();

                        return (bool)responseContent.Object;
                    }
                    else
                    {
                        throw new Exception($"Erro na requisição. Código de status: {response.StatusCode}");
                    }

                }
            }
            catch (System.Exception e)
            {
                throw new Exception("Erro ao processar a requisição.", e);
            }

        }
    }


}
