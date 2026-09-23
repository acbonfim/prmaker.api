using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using solvace.prform.domain.Entities;

namespace solvace.prform.api.Auditing;

/// <summary>
/// Preenche automaticamente os campos de auditoria de <see cref="IAuditableEntity"/> a cada
/// SaveChanges: CreatedAt/CreatedBy nas inclusões e UpdatedAt/UpdatedBy nas alterações.
/// O "quem" vem do usuário autenticado (claim ExternalId do token x-api-key).
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditSaveChangesInterceptor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyAudit(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAudit(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAudit(DbContext? context)
    {
        if (context is null) return;

        var user = CurrentUser();
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreatedAt == default)
                        entry.Entity.CreatedAt = now;
                    if (string.IsNullOrEmpty(entry.Entity.CreatedBy) && !string.IsNullOrEmpty(user))
                        entry.Entity.CreatedBy = user;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    if (!string.IsNullOrEmpty(user))
                        entry.Entity.UpdatedBy = user;
                    break;
            }
        }
    }

    /// <summary>ExternalId do usuário autenticado (fallback para NameIdentifier/Name).</summary>
    private string? CurrentUser()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        return principal.FindFirstValue("ExternalId")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(ClaimTypes.Name);
    }
}
