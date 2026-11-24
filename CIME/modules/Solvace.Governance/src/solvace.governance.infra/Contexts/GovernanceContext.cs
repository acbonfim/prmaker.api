using System.Text.Json;
using Cime.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using solvace.governance.domain.Entities;

namespace solvace.governance.infra.Contexts;

/// <summary>0098: governança (regras, hooks, configurações e a trilha de eventos), schema "governance".</summary>
public class GovernanceContext : DbContext
{
    public GovernanceContext(DbContextOptions<GovernanceContext> options) : base(options)
    {
    }

    public DbSet<GovernanceEvent> Events { get; set; }
    public DbSet<GovernanceRule> Rules { get; set; }
    public DbSet<GovernanceHook> Hooks { get; set; }
    public DbSet<GovernanceSettings> Settings { get; set; }

    public const string Schema = "governance";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.UseUnspecifiedDateTimes();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<GovernanceEvent>(e =>
        {
            e.ToTable("GovernanceEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.Property(x => x.UserName).HasMaxLength(GovernanceEvent.MaxNameLength).IsRequired();
            e.Property(x => x.Role).HasMaxLength(100);
            e.Property(x => x.Source).HasMaxLength(20).IsRequired();
            e.Property(x => x.Action).HasMaxLength(GovernanceEvent.MaxActionLength).IsRequired();
            e.Property(x => x.Category).HasMaxLength(40).IsRequired();
            e.Property(x => x.Phase).HasMaxLength(20);
            e.Property(x => x.StepKey).HasMaxLength(100);
            e.Property(x => x.Outcome).HasMaxLength(20).IsRequired();
            e.Property(x => x.Rules).HasColumnType("text[]");
            e.Property(x => x.Detail).HasMaxLength(GovernanceEvent.MaxDetailLength);
            e.Property(x => x.SessionId).HasMaxLength(100);
            e.Property(x => x.Machine).HasMaxLength(GovernanceEvent.MaxNameLength);
            e.HasIndex(x => x.StartedAt);
            e.HasIndex(x => new { x.CardNumber, x.StartedAt });
            e.HasIndex(x => new { x.UserId, x.StartedAt });
            e.HasIndex(x => new { x.Outcome, x.StartedAt });
        });

        modelBuilder.Entity<GovernanceRule>(e =>
        {
            e.ToTable("GovernanceRules");
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(80).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasColumnType("text").IsRequired();
            e.Property(x => x.Category).HasMaxLength(40).IsRequired();
            e.Property(x => x.Effect).HasMaxLength(20).IsRequired();
            e.Property(x => x.Message).HasColumnType("text").IsRequired();
            e.Property(x => x.Scope).HasColumnType("jsonb").HasConversion(JsonConverter<RuleScope>(), JsonComparer<RuleScope>());
            e.Property(x => x.Match).HasColumnType("jsonb").HasConversion(JsonConverter<RuleMatch>(), JsonComparer<RuleMatch>());
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
        });

        modelBuilder.Entity<GovernanceHook>(e =>
        {
            e.ToTable("GovernanceHooks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(80).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasColumnType("text").IsRequired();
            e.Property(x => x.Event).HasMaxLength(120).IsRequired();
            e.Property(x => x.Matcher).HasMaxLength(300).IsRequired();
            e.Property(x => x.Action).HasMaxLength(20).IsRequired();
            e.Property(x => x.Config).HasColumnType("jsonb").HasConversion(JsonConverter<Dictionary<string, string>>(), JsonComparer<Dictionary<string, string>>());
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
        });

        modelBuilder.Entity<GovernanceSettings>(e =>
        {
            e.ToTable("GovernanceSettings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Mode).HasMaxLength(20).IsRequired();
            e.Property(x => x.ExemptRoles).HasColumnType("jsonb").HasConversion(JsonConverter<List<string>>(), JsonComparer<List<string>>());
            e.Property(x => x.RecordSources).HasColumnType("jsonb").HasConversion(JsonConverter<List<string>>(), JsonComparer<List<string>>());
            e.Property(x => x.Milestones).HasColumnType("jsonb").HasConversion(JsonConverter<List<Milestone>>(), JsonComparer<List<Milestone>>());
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
        });
    }

    /// <summary>JSON antigo sem a chave/coleção vem null (lição do hotfix #77): cai no valor novo.</summary>
    private static ValueConverter<T, string> JsonConverter<T>() where T : class, new() =>
        new(v => JsonSerializer.Serialize(v, Json), v => JsonSerializer.Deserialize<T>(v, Json) ?? new T());

    private static ValueComparer<T> JsonComparer<T>() where T : class, new() =>
        new((a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
            v => JsonSerializer.Serialize(v, Json).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json) ?? new T());
}
