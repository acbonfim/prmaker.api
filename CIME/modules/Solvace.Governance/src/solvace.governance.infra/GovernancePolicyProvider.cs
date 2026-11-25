using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using solvace.governance.application.Contracts;
using solvace.governance.domain.Entities;
using solvace.governance.infra.Contexts;

namespace solvace.governance.infra;

/// <summary>
/// 0098: fotografia das regras/hooks/configurações em memória, renovada a cada 30 s (o Cloud Run pode ter mais de uma
/// instância: a que gravou invalida na hora, as outras pegam a mudança em até 30 s). Sem banco, devolve a última
/// fotografia; sem nenhuma, o padrão (modo monitor, sem regras) — a governança nunca derruba a API.
/// </summary>
public sealed class GovernancePolicyProvider(IServiceScopeFactory scopes, TimeProvider clock) : IGovernancePolicyProvider
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GovernancePolicy? _current;
    private DateTimeOffset _loadedAt;

    public async Task<GovernancePolicy> GetAsync(CancellationToken ct = default)
    {
        var snapshot = _current;
        if (snapshot is not null && clock.GetUtcNow() - _loadedAt < Ttl) return snapshot;

        await _gate.WaitAsync(ct);
        try
        {
            if (_current is not null && clock.GetUtcNow() - _loadedAt < Ttl) return _current;
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<GovernanceContext>();
                var rules = await db.Rules.AsNoTracking().OrderBy(r => r.Order).ToListAsync(ct);
                var hooks = await db.Hooks.AsNoTracking().OrderBy(h => h.Order).ToListAsync(ct);
                var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new GovernanceSettings();
                var standards = await db.Standards.AsNoTracking().Where(x => x.Status != StandardStatus.Deprecated).OrderBy(x => x.Order).ToListAsync(ct);
                var stamp = string.Join(';',
                    rules.Select(r => $"{r.Key}:{r.UpdatedAt?.ToUnixTimeMilliseconds()}")
                        .Concat(hooks.Select(h => $"{h.Key}:{h.UpdatedAt?.ToUnixTimeMilliseconds()}"))
                        .Append($"s:{settings.UpdatedAt?.ToUnixTimeMilliseconds()}")
                        .Append($"p:{standards.Count}:{standards.Select(x => x.UpdatedAt).DefaultIfEmpty().Max()?.ToUnixTimeMilliseconds()}"));
                var version = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stamp)))[..12].ToLowerInvariant();
                _current = new GovernancePolicy(rules, hooks, settings, version) { Standards = standards };
            }
            catch (Exception) when (_current is not null)
            {
                // Banco fora: mantém a última fotografia por mais um ciclo.
            }
            catch (Exception)
            {
                _current = new GovernancePolicy([], [], new GovernanceSettings(), "default");
            }
            _loadedAt = clock.GetUtcNow();
            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _loadedAt = DateTimeOffset.MinValue;
}
