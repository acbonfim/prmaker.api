using cliqx.auth.api.Dtos;
using cliqx.auth.api.Models;
using cliqx.auth.api.Services.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace cliqx.auth.api.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    [Authorize("Bearer", Roles = "admin,support")]
    public class ServiceController : ControllerBase
    {
        public MyServicesService _service { get; }
        public ServiceController(MyServicesService service)
        {
            _service = service;
        }

        [HttpPost]
        public async Task<ActionResult> Create(ServiceDto service)
        {
            var ret = await _service.Create(service);

            if(ret.Success)
                return StatusCode(StatusCodes.Status201Created,ret);

            return BadRequest();
        }

        [HttpPut]
        public async Task<ActionResult> Update(ServiceDto service)
        {
            var ret = await _service.Update(service);

            if(ret.Success)
                return StatusCode(StatusCodes.Status200OK,ret);

            return StatusCode(StatusCodes.Status400BadRequest,ret);
        }

        [HttpDelete]
        public async Task<ActionResult> Delete([FromQuery] string id)
        {
            var ret = await _service.Delete(id);

            if(ret.Success)
                return StatusCode(StatusCodes.Status200OK,ret);

            return StatusCode(StatusCodes.Status400BadRequest,ret);
        }

        [HttpGet]
        public async Task<ActionResult> GetAll()
        {
            var ret = await _service.GetAllServices();

            if(ret.Success)
                return StatusCode(StatusCodes.Status200OK,ret);

            return BadRequest();
        }

        [HttpGet]
        public async Task<ActionResult> GetAllByUserId(int userId)
        {
            var ret = await _service.GetAllByUserId(userId);

            if(ret.Success)
                return StatusCode(StatusCodes.Status200OK,ret);

            return BadRequest();
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult> HasAccessToService(int userId, string serviceExternalId)
        {
            var ret = await _service.HasAccessToService(userId, serviceExternalId);

            if(ret.Success)
                return StatusCode(StatusCodes.Status200OK,ret);

            return BadRequest();
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<ActionResult> HasAccessToServices([FromQuery]int userId, [FromBody]string[] services)
        {
            var ret = await _service.HasAccessToService(userId, null, services);

            if(ret.Success)
                return StatusCode(StatusCodes.Status200OK,ret);

            return BadRequest();
        }

        [HttpPost]
        public async Task<ActionResult> AddUserToService([FromQuery]int userId, [FromQuery] string serviceId)
        {
            var ret = await _service.AddUserToService(userId, serviceId);

            if(ret.Success)
                return StatusCode(StatusCodes.Status200OK,ret);

            return StatusCode(StatusCodes.Status400BadRequest,ret);
        }

        [HttpDelete]
        public async Task<ActionResult> RemoveUserFromService([FromQuery]int userId, [FromQuery] string serviceId)
        {
            var ret = await _service.RemoveUserFromService(userId, serviceId);

            if(ret.Success)
                return StatusCode(StatusCodes.Status200OK,ret);

            return StatusCode(StatusCodes.Status400BadRequest,ret);
        }
    }
}