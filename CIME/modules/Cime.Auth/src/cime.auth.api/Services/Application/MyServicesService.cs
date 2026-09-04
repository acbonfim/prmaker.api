using cliqx.auth.api.Dtos;
using cliqx.auth.api.Extensions;
using cliqx.auth.api.Models;
using cliqx.auth.api.Models.Identity;
using Microsoft.EntityFrameworkCore;
using ProSales.Repository.Contexts;

namespace cliqx.auth.api.Services.Application
{
    public class MyServicesService
    {
        private readonly DefaultContext _context;

        public MyServicesService(DefaultContext context)
        {
            _context = context;
        }

        private static ServiceDto ToDto(MyService s) => new ServiceDto
        {
            ExternalId = s.ExternalId,
            Name = s.Name,
            Description = s.Description,
            IsActive = s.IsActive
        };

        public async Task<RetornoDto> Create(ServiceDto dto)
        {
            try
            {
                var service = new MyService
                {
                    Name = dto.Name,
                    Description = dto.Description
                };

                await _context.AddAsync(service);

                if (await _context.SaveChangesAsync() > 0)
                    return ToDto(service).AsRetornoDto("Ok", true);

                throw new Exception();
            }
            catch (System.Exception e)
            {
                return e.AsRetornoDto("Erro ao tentar salvar", false);
                throw;
            }
        }

        public async Task<RetornoDto> Update(ServiceDto dto)
        {
            try
            {
                var found = await _context.Services
                    .FirstOrDefaultAsync(x => x.ExternalId == dto.ExternalId);

                if (found is null)
                    return new object().AsRetornoDto($"Service not found with externalId {dto.ExternalId}.");

                found.Name = dto.Name;
                found.Description = dto.Description;

                if (await _context.SaveChangesAsync() >= 0)
                    return ToDto(found).AsRetornoDto("Ok", true);

                throw new Exception();
            }
            catch (System.Exception e)
            {
                return e.AsRetornoDto("Erro ao tentar atualizar", false);
            }
        }

        public async Task<RetornoDto> Delete(string externalId)
        {
            try
            {
                var found = await _context.Services
                    .FirstOrDefaultAsync(x => x.ExternalId == externalId);

                if (found is null)
                    return new object().AsRetornoDto($"Service not found with id {externalId}.");

                // Remove os vínculos de usuários antes de excluir o serviço (FK obrigatória).
                var links = await _context.UserServices
                    .Where(x => x.ServiceId == found.Id)
                    .ToListAsync();

                if (links.Count > 0)
                    _context.UserServices.RemoveRange(links);

                _context.Services.Remove(found);

                if (await _context.SaveChangesAsync() > 0)
                    return new { id = found.ExternalId }.AsRetornoDto("Ok", true);

                throw new Exception();
            }
            catch (System.Exception e)
            {
                return e.AsRetornoDto("Erro ao tentar excluir", false);
            }
        }

        public async Task<RetornoDto> GetAllServices()
        {
            try
            {
                var result = await _context.Services
                    .Select(s => new ServiceDto
                    {
                        ExternalId = s.ExternalId,
                        Name = s.Name,
                        Description = s.Description,
                        IsActive = s.IsActive
                    })
                    .ToListAsync();

                return result.AsRetornoDto("Ok", true);
            }
            catch (System.Exception e)
            {
                return e.AsRetornoDto("Erro ao tentar salvar", false);
                throw;
            }
        }

        public async Task<RetornoDto> GetAllByUserId(int userId)
        {
            try
            {
                var result = await _context.Services
                    .Where(x => x.UserServices.Any(u => u.UserId == userId))
                    .Select(s => new ServiceDto
                    {
                        ExternalId = s.ExternalId,
                        Name = s.Name,
                        Description = s.Description,
                        IsActive = s.IsActive
                    })
                    .ToListAsync();

                return result.AsRetornoDto("Ok", true);
            }
            catch (System.Exception e)
            {
                return e.AsRetornoDto("Erro ao tentar salvar", false);
                throw;
            }
        }

        public async Task<RetornoDto> HasAccessToService(int userId, string? serviceExternalId = null, string[]? services = null)
        {
            IQueryable<UserService> query = _context.UserServices;

            if (serviceExternalId is not null)
            {
                query = query.Where(x => x.UserId == userId
                    && x.Service.ExternalId == serviceExternalId);
            }

            if (services is not null)
            {
                query = query.Where(x => x.UserId == userId
                    && services.Contains(x.Service.ExternalId));
            }


            var result = query.Any().AsRetornoDto("Ok", true);

            return result;
        }

        public async Task<RetornoDto> RemoveUserFromService(int userId, string serviceExternalId)
        {
            try
            {
                var service = await _context.Services
                    .FirstOrDefaultAsync(x => x.ExternalId == serviceExternalId);

                if (service is null)
                    return new object().AsRetornoDto($"Service not found with id {serviceExternalId}.");

                var link = await _context.UserServices
                    .FirstOrDefaultAsync(x => x.UserId == userId && x.ServiceId == service.Id);

                if (link is null)
                    return new object().AsRetornoDto("Usuário não possui acesso a este serviço.");

                _context.UserServices.Remove(link);

                if (await _context.SaveChangesAsync() > 0)
                    return new { userId, serviceId = serviceExternalId }.AsRetornoDto("Ok", true);

                throw new Exception();
            }
            catch (System.Exception e)
            {
                return e.AsRetornoDto("Erro ao remover acesso", false);
            }
        }

        public async Task<RetornoDto> AddUserToService(int userId, string serviceExternalId)
        {
            IQueryable<MyService> queryServices = _context.Services;

            var resultServices = queryServices.FirstOrDefault(x => x.ExternalId == serviceExternalId);

            if (resultServices is null)
                return new object().AsRetornoDto($"Service not found with id {serviceExternalId}.");

            IQueryable<User> queryUser = _context.Users;

            var resultUser = queryUser.FirstOrDefault(x => x.Id == userId);

            if (resultUser is null)
                return new object().AsRetornoDto($"User not found with id {userId}.");

            IQueryable<UserService> userServiceFoundQuery = _context.UserServices;

            var userServiceFound = userServiceFoundQuery.FirstOrDefault(x => x.UserId == userId
                    && x.Service.Id == resultServices.Id);

            if (userServiceFound is not null)
                return new object().AsRetornoDto($"User already access to service.");


            var userService = new UserService
            {
                ServiceId = resultServices.Id,
                UserId = resultUser.Id
            };

            await _context.AddAsync(userService);

            if (await _context.SaveChangesAsync() > 0)
            {
                return new
                {
                    User = new
                    {
                        Id = userService.UserId,
                        Name = userService.User.FullName,
                    },
                    Service = new
                    {
                        Id = userService.ServiceId,
                        Name = userService.Service.Name
                    }
                }.AsRetornoDto("Ok", true);
            }

            return new{}.AsRetornoDto($"Erro to perform action"); ;
        }
    }
}