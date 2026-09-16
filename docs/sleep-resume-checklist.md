# Sleep/resume checklist

This is a manual check for the Windows hardware that will host a long-lived world. The sleep contract is explicit: suspension pauses the process and causes no wall-time catch-up. On resume, the service continues from its last in-memory state and configured simulation rate; it does not simulate the minutes spent asleep.

## Before suspending

- [ ] Confirm the service is the only Little Ages process using the active world database.
- [ ] Confirm `GET http://127.0.0.1:5274/api/v1/health` is Healthy and `/api/v1/status` reports `State=Running` and `PersistenceState=Healthy`.
- [ ] Record the current `WorldMinute`, `LastSuccessfulCheckpointWorldMinute`, and `LastSuccessfulCheckpointUtc` from `/api/v1/status`.
- [ ] Confirm the active world and `DataRoot` are the intended values, and that a recent backup exists under the backup runbook.
- [ ] Keep an observer browser connected and record the most recent `worldChanged` revision if the test client displays it. The SignalR channel is observer-only; REST remains authoritative.
- [ ] Note the configured `SimulationMinutesPerSecond` and the planned suspension duration. Do not infer a target world minute by multiplying the sleep duration; the expected simulated advance during suspension is zero.

## Suspend and resume

- [ ] Put Windows to sleep using the normal hardware/power-management path. Do not stop or kill the service for this check.
- [ ] Leave the machine suspended for a measured interval appropriate to the target hardware.
- [ ] Resume Windows and wait for the service process and network listener to become available.
- [ ] Verify the service remains running. If it is not running, treat this as a crash/restart test and follow the recovery runbook rather than declaring sleep behavior passed.
- [ ] Query `/api/v1/status` immediately after resume. The first post-resume `WorldMinute` must not include the suspended wall-time interval; it should be the pre-sleep progression plus only work performed before suspension and after resume.
- [ ] Observe several post-resume ticks. The world should advance again at the configured rate, with no burst that represents catch-up for sleep.
- [ ] Confirm `/api/v1/health` returns Healthy and persistence returns to Healthy after any normal checkpoint. Check that `LastSuccessfulCheckpointWorldMinute` advances normally.
- [ ] Confirm the browser reconnects to `/hubs/world` if needed, receives only coalesced invalidations, and refetches authoritative REST observations. If SignalR remains unavailable, REST polling must still show the correct world.

## Pass/fail evidence

Record the hardware/model, Windows build, service version, configured rate, pre-sleep and post-resume timestamps, pre-sleep/post-resume world minutes, health/status payloads, and any relevant structured log entries. Pass only when the measured world-minute difference excludes the suspension interval and the host resumes without data loss or an unexplained state transition.

If the process crashed, the machine rebooted, a final checkpoint was not confirmed, or the database is rejected on reopen, record that as a separate recovery result. Do not repair or regenerate the database to make this checklist pass; preserve the files and use [`backup-and-recovery.md`](backup-and-recovery.md).
