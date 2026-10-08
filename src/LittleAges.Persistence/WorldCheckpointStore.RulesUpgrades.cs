using System.Globalization;
using System.Text.Json;
using LittleAges.Simulation;
using Microsoft.EntityFrameworkCore;

namespace LittleAges.Persistence;

public sealed record WorldRulesUpgradeReceipt(string SourceRules, string TargetRules, long ActivationMinute,
    string ConvertersJson, string SourceFingerprint, string TargetFingerprint, string BackupPath, string BackupSha256);

public sealed partial class WorldCheckpointStore
{
    public Task UpgradeRulesAsync(WorldRulesUpgradePlan plan, WorldRulesBackup backup, CancellationToken cancellationToken = default) =>
        UpgradeRulesCoreAsync(plan, backup, null, cancellationToken);

    internal Task UpgradeRulesAsync(WorldRulesUpgradePlan plan, WorldRulesBackup backup, CheckpointFailurePoint failurePoint,
        CancellationToken cancellationToken = default) => UpgradeRulesCoreAsync(plan, backup, failurePoint, cancellationToken);

    private async Task UpgradeRulesCoreAsync(WorldRulesUpgradePlan plan, WorldRulesBackup backup,
        CheckpointFailurePoint? failurePoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(backup);
        if (plan.Converters.Count == 0) throw new ArgumentException("A rules upgrade requires a forward conversion.", nameof(plan));
        var source = await LoadAsync(cancellationToken);
        await backup.VerifyAsync(source, cancellationToken);
        await CheckpointCoreAsync(plan.Snapshot, DateTime.UtcNow, failurePoint, cancellationToken, plan, backup);
    }

    private async Task ValidateRulesUpgradeAsync(WorldRulesUpgradePlan plan, CancellationToken cancellationToken)
    {
        var source = await LoadAsync(cancellationToken);
        if (WorldRulesUpgrades.Fingerprint(source) != plan.SourceFingerprint || source.SimulationRulesVersion != plan.SourceRules)
            throw new InvalidDataException("The saved checkpoint changed after its rules upgrade was planned.");
        var authorized = WorldRulesUpgrades.Plan(source, plan.TargetRules);
        if (authorized.TargetFingerprint != plan.TargetFingerprint || WorldRulesUpgrades.Fingerprint(plan.Snapshot) != authorized.TargetFingerprint ||
            !authorized.Converters.SequenceEqual(plan.Converters, StringComparer.Ordinal))
            throw new InvalidDataException("Only a registered, validated rules conversion can replace this checkpoint.");
    }

    private Task<int> WriteRulesUpgradeReceiptAsync(WorldRulesUpgradePlan plan, WorldRulesBackup backup,
        DateTime upgradedUtc, CancellationToken cancellationToken) => _context.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO world_rules_upgrades (source_rules, target_rules, activation_minute, converters_json,
            source_fingerprint, target_fingerprint, backup_path, backup_sha256, upgraded_utc)
        VALUES ({plan.SourceRules}, {plan.TargetRules}, {plan.Snapshot.WorldMinute.Value}, {JsonSerializer.Serialize(plan.Converters)},
            {plan.SourceFingerprint}, {plan.TargetFingerprint}, {backup.Path}, {backup.Sha256},
            {upgradedUtc.ToString("O", CultureInfo.InvariantCulture)});
        """, cancellationToken);

    public async Task<WorldRulesUpgradeReceipt?> ReadLatestRulesUpgradeAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT source_rules, target_rules, activation_minute, converters_json, source_fingerprint,
                target_fingerprint, backup_path, backup_sha256 FROM world_rules_upgrades ORDER BY id DESC LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetString(3), reader.GetString(4),
            reader.GetString(5), reader.GetString(6), reader.GetString(7));
    }
}
