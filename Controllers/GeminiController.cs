using Microsoft.AspNetCore.Mvc;
using solvace.prform.Services;

namespace solvace.prform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GeminiController : ControllerBase
    {
        private readonly IGeminiService _geminiService;

        public GeminiController(IGeminiService geminiService)
        {
            _geminiService = geminiService;
        }

        [HttpPost("generate")]
        public async Task<IActionResult> Generate([FromBody] string prompt)
        {
            var result = await _geminiService.GenerateContentAsync(prompt);
            return Ok(new { response = result });
        }
    }
}
