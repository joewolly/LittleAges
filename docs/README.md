# Little Ages documentation

This index separates current implementation references from preserved design
baselines, operational runbooks, and measured acceptance evidence. When a
document describes an earlier milestone or a proposed feature, its role is
historical or design context rather than a claim about the current runtime.

## Current implementation

- [`architecture.md`](./architecture.md) — current M0–M8 project boundaries,
  canonical ownership, persistence, server lifecycle, observer delivery, CI
  split, and implemented/deferred milestone context.
- [`simulation-model.md`](./simulation-model.md) — current deterministic
  calendar, identity, RNG, event ordering, simulation systems, persistence
  compatibility, history, and headless boundaries.

## Product and design references

- [`design-v0.1.md`](./design-v0.1.md) — the preserved v0.1 product-design
  baseline: vision, principles, intended scope, non-goals, release criteria,
  and post-v0.1 direction. It is not a live feature checklist.
- [`implementation-plan-v0.1.md`](./implementation-plan-v0.1.md) — the
  preserved technical plan and decision record, including milestone gates and
  candidate implementation notes. It is not a deployment runbook.

## Acceptance and compatibility evidence

- [`v0.1-acceptance-report.md`](./v0.1-acceptance-report.md) — candidate-only
  seed-42 acceptance evidence with exact metrics, fingerprints, provenance,
  invariants, and the Section 62 matrix. Exact measured values are preserved;
  manual target-Windows and browser checks remain identified as such.

## Deployment and operations

- [`windows-service.md`](./windows-service.md) — Windows Service installation,
  trusted-LAN binding, publishing, packaging, logs, and removal.
- [`windows-service.example.json`](./windows-service.example.json) — portable
  example configuration with loopback defaults and no machine-specific data.
- [`backup-and-recovery.md`](./backup-and-recovery.md) — SQLite WAL-aware
  backup, restore, and integrity guidance.
- [`sleep-resume-checklist.md`](./sleep-resume-checklist.md) — manual
  target-hardware suspension/resume verification and evidence requirements.

## Automation references

- [`../.github/workflows/ci.yml`](../.github/workflows/ci.yml) — fast pull
  request validation, excluding `Category=Long` backend tests.
- [`../.github/workflows/long-tests.yml`](../.github/workflows/long-tests.yml)
  — scheduled/manual long-test validation.
- [`../.github/workflows/v01-acceptance.yml`](../.github/workflows/v01-acceptance.yml)
  — manual Windows headless acceptance workflow.
- [`../.github/workflows/windows-package.yml`](../.github/workflows/windows-package.yml)
  — manual candidate Windows package workflow.
