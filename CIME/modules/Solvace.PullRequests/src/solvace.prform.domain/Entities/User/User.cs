using System.ComponentModel.DataAnnotations.Schema;

namespace solvace.prform.domain.Entities.User;

[Table("AspNetUsers")]
public class User
{
    [Column("ExternalId")]
    public Guid Id { get; set; }
    
    public string FullName { get; set; }
    
    [Column("Departamento")]
    public string? Department { get; set; }
}