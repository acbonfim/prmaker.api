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

    /// <summary>Último login com senha (DataUltimoLogin da auth, sem fuso) — "visto há" da gestão (0079).</summary>
    [Column("DataUltimoLogin")]
    public DateTime? LastLoginAt { get; set; }
}