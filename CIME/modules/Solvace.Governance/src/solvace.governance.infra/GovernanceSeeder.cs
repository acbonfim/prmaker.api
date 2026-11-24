using Microsoft.EntityFrameworkCore;
using solvace.governance.domain;
using solvace.governance.domain.Entities;
using solvace.governance.infra.Contexts;

namespace solvace.governance.infra;

/// <summary>
/// 0098: semeia as regras e hooks embutidos e a linha de configurações. Roda no <c>--migrate</c>, depois das migrações;
/// acrescenta só o que falta pela chave e nunca sobrescreve o que o admin mudou.
/// </summary>
public static class GovernanceSeeder
{
    public static async Task<int> SeedAsync(GovernanceContext db, DateTimeOffset now, CancellationToken ct = default)
    {
        var added = 0;
        var ruleKeys = await db.Rules.Select(r => r.Key).ToListAsync(ct);
        foreach (var def in GovernanceDefaults.Rules.Where(d => !ruleKeys.Contains(d.Key)))
        {
            db.Rules.Add(GovernanceDefaults.CreateRule(def, now));
            added++;
        }

        var hookKeys = await db.Hooks.Select(h => h.Key).ToListAsync(ct);
        var order = 10;
        foreach (var def in GovernanceDefaults.Hooks)
        {
            if (!hookKeys.Contains(def.Key))
            {
                db.Hooks.Add(GovernanceDefaults.CreateHook(def, order, now));
                added++;
            }
            order += 10;
        }

        if (!await db.Settings.AnyAsync(ct))
        {
            db.Settings.Add(new GovernanceSettings());
            added++;
        }

        if (added > 0) await db.SaveChangesAsync(ct);
        return added;
    }
}
