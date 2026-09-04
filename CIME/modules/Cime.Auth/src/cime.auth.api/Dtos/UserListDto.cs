using System.Collections.Generic;

namespace cliqx.auth.api.Dtos
{
    // Item de usuário retornado na listagem/gestão de usuários.
    // Diferente do UserDto (usado no cadastro/login), aqui expomos o Id numérico,
    // o status Active e os cargos, que a tela de gestão precisa.
    public class UserListItemDto
    {
        public int Id { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public string FullName { get; set; }
        public string Departamento { get; set; }
        public bool Active { get; set; }
        public Guid ExternalId { get; set; }
        public int CompanyId { get; set; }
        public string ChannelOrigin { get; set; }
        public List<UserRoleItemDto> UserRoles { get; set; } = new();
    }

    public class UserRoleItemDto
    {
        public RoleItemDto Role { get; set; }
    }

    public class RoleItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    // Envelope de paginação consumido pela tela de gestão (elements + stillFetchable).
    public class PagedResultDto<T>
    {
        public IEnumerable<T> Elements { get; set; } = new List<T>();
        public int Page { get; set; }
        public int ItemsPerPage { get; set; }
        public int Total { get; set; }
        public int TotalPages { get; set; }
        public bool StillFetchable { get; set; }
    }
}
