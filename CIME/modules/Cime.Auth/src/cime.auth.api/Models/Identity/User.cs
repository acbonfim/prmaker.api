using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace cliqx.auth.api.Models.Identity;

public class User : IdentityUser<int>
{
    public string FullName { get; set; }
    public string Departamento { get; set; }
    public DateTime DataUltimoLogin { get; set; }
    public List<UserRole> UserRoles { get; set; }
    public bool Active { get; set; } = true;
    public Guid ExternalId { get; set; } = Guid.NewGuid();
    public int CompanyId { get; set; }
    public string ChannelOrigin { get; set; }
    public List<UserService>? UserServices { get; set; }
}
