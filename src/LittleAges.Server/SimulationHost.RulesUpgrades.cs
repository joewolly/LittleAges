using LittleAges.Persistence;
using LittleAges.Simulation;

namespace LittleAges.Server;

public sealed record ServerRulesUpgradeStatus(string State, string SourceRules, string TargetRules,
    long? ActivationMinute, string Message);

public sealed partial class SimulationHost
{
    private ServerRulesUpgradeStatus? _rulesUpgradeStatus;
    internal Func<string, Exception?>? RulesUpgradeFailureHookForTesting { get; set; }

    private ServerRulesUpgradeStatus InitialRulesUpgradeStatus(string rules)
    {
        var target = SimulationEngine.CurrentSimulationRulesVersion;
        if (!_options.AutoUpgradeWorldRules)
            return new("Preserved", rules, target, null, "Automatic feature upgrades are disabled.");
        if (rules == target)
            return new("Current", rules, target, null, "This world has the current simulation features.");
        if (!WorldRulesUpgrades.CanUpgrade(rules))
            return new("Unsupported", rules, target, null, "This older world continues with its saved rules; an automatic feature upgrade is unavailable.");
        return new("Current", rules, target, null, "New world created with its selected rules.");
    }

    private void ThrowRulesUpgradeFailureForTesting(string stage)
    {
        if (RulesUpgradeFailureHookForTesting?.Invoke(stage) is { } exception) throw exception;
    }

    private async Task<SimulationPersistenceSnapshot> UpgradeWorldRulesAsync(SimulationPersistenceSnapshot source,
        WorldRulesBackup? backup, Exception? backupFailure, CancellationToken cancellationToken)
    {
        var target = SimulationEngine.CurrentSimulationRulesVersion;
        if (backupFailure is not null && backup is not null && WorldRulesUpgrades.Fingerprint(source) != backup.CheckpointFingerprint)
            throw new InvalidDataException("Upgrade preparation changed the backed-up checkpoint; refusing to advance the world.", backupFailure);
        if (!_options.AutoUpgradeWorldRules)
        {
            if (backupFailure is not null) LogRulesUpgradeFailed(source.SimulationRulesVersion, target, BackupLocation(backup), backupFailure);
            _rulesUpgradeStatus = new("Preserved", source.SimulationRulesVersion, target, null, "Automatic feature upgrades are disabled.");
            return source;
        }
        if (source.SimulationRulesVersion == target)
        {
            if (backupFailure is not null)
            {
                // A schema change was skipped; the operational receipt table may not exist yet.
                _rulesUpgradeStatus = new("Failed", target, target, null, "The feature upgrade could not be applied. Your world continues safely with its previous rules.");
                LogRulesUpgradeFailed(target, target, BackupLocation(backup), backupFailure);
                return source;
            }
            var receipt = await _checkpointStore!.ReadLatestRulesUpgradeAsync(cancellationToken);
            _rulesUpgradeStatus = new("Current", receipt?.SourceRules ?? target, target, receipt?.ActivationMinute, "This world has the current simulation features.");
            return source;
        }
        if (!WorldRulesUpgrades.CanUpgrade(source.SimulationRulesVersion))
        {
            _rulesUpgradeStatus = new("Unsupported", source.SimulationRulesVersion, target, null, "This older world continues with its saved rules; an automatic feature upgrade is unavailable.");
            LogRulesUpgradeUnavailable(source.SimulationRulesVersion, target);
            return source;
        }
        var originalFingerprint = WorldRulesUpgrades.Fingerprint(source);
        WorldRulesUpgradePlan? plan = null;
        try
        {
            if (backupFailure is not null) throw new InvalidOperationException("Rules upgrade preparation failed.", backupFailure);
            if (backup is null) throw new InvalidDataException("An eligible world has no verified pre-upgrade backup.");
            ThrowRulesUpgradeFailureForTesting("conversion");
            plan = WorldRulesUpgrades.Plan(source);
            await _checkpointStore!.UpgradeRulesAsync(plan, backup, cancellationToken);
            ThrowRulesUpgradeFailureForTesting("afterCommit");
            var committed = await _checkpointStore.LoadAsync(cancellationToken);
            if (WorldRulesUpgrades.Fingerprint(committed) != plan.TargetFingerprint)
                throw new InvalidDataException("The upgraded checkpoint did not match its validated conversion.");
            _rulesUpgradeStatus = new("Upgraded", source.SimulationRulesVersion, target, source.WorldMinute.Value, "New simulation features are active. Your civilization was preserved.");
            LogRulesUpgraded(source.SimulationRulesVersion, target, source.WorldMinute.Value, backup.Path);
            return committed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Never resume an in-memory guess after a failed or uncertain write.
            var committed = await _checkpointStore!.LoadAsync(cancellationToken);
            var fingerprint = WorldRulesUpgrades.Fingerprint(committed);
            if (fingerprint == originalFingerprint)
            {
                _rulesUpgradeStatus = new("Failed", source.SimulationRulesVersion, target, null, "The feature upgrade could not be applied. Your world continues safely with its previous rules.");
                LogRulesUpgradeFailed(source.SimulationRulesVersion, target, BackupLocation(backup), exception);
                return committed;
            }
            var receipt = await _checkpointStore.ReadLatestRulesUpgradeAsync(cancellationToken);
            if (plan is not null && fingerprint == plan.TargetFingerprint && receipt is not null &&
                receipt.SourceFingerprint == originalFingerprint && receipt.TargetFingerprint == fingerprint)
            {
                _rulesUpgradeStatus = new("Upgraded", source.SimulationRulesVersion, target, source.WorldMinute.Value, "The committed feature upgrade was verified. Your civilization was preserved.");
                LogRulesUpgraded(source.SimulationRulesVersion, target, source.WorldMinute.Value, receipt.BackupPath);
                return committed;
            }
            throw new InvalidDataException("Rules upgrade outcome is uncertain; refusing to advance the world.", exception);
        }
    }

    private string BackupLocation(WorldRulesBackup? backup) => backup?.Path ?? Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(_options.DatabasePath))!, "rules-upgrade-backups");

    [LoggerMessage(EventId = 150, Level = LogLevel.Information, Message = "World rules upgraded from {SourceRules} to {TargetRules} at minute {WorldMinute}. Verified backup: {BackupPath}")]
    private partial void LogRulesUpgraded(string sourceRules, string targetRules, long worldMinute, string backupPath);

    [LoggerMessage(EventId = 151, Level = LogLevel.Warning, Message = "World rules upgrade from {SourceRules} to {TargetRules} failed; the original checkpoint was validated and resumed. Backup: {BackupPath}")]
    private partial void LogRulesUpgradeFailed(string sourceRules, string targetRules, string? backupPath, Exception exception);

    [LoggerMessage(EventId = 152, Level = LogLevel.Warning, Message = "World rules {SourceRules} have no automatic upgrade path to {TargetRules}; continuing saved rules.")]
    private partial void LogRulesUpgradeUnavailable(string sourceRules, string targetRules);
}
