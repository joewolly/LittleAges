# Little Ages documentation

This index separates current implementation references from preserved design
baselines, operational runbooks, and measured acceptance evidence. When a
document describes an earlier milestone or a proposed feature, its role is
historical or design context rather than a claim about the current runtime.

## Release notes

- [`release-v0.3.0.md`](./release-v0.3.0.md) — draft release notes for the M14
  candidate in PR #15; the release has not been published.

## Current implementation

- [`m14-migration.md`](./m14-migration.md) — implemented M14 rules contract and
  current new-world default for migration and a second settlement; long-horizon
  acceptance status is documented there.
- [`living-settlement-v0.2.md`](./living-settlement-v0.2.md) — opt-in successor
  rules, including the `living1` and opt-in new-world `living2` identifiers,
  autonomous work and economy, personal life, environment, knowledge,
  persistence, and observer contracts.

- [`architecture.md`](./architecture.md) — project boundaries, canonical
  ownership, persistence, server lifecycle, observer delivery, and CI split.
- [`simulation-model.md`](./simulation-model.md) — current deterministic
  calendar, identity, RNG, event ordering, simulation systems, persistence
  compatibility, history, and headless boundaries.

## Product and design references

- [`feature-ideas.md`](./feature-ideas.md) — editable ideas for future
  simulation, history, world, and observer features; not a release schedule.
- [`design-v0.1.md`](./design-v0.1.md) — the preserved v0.1 product-design
  baseline: vision, principles, intended scope, non-goals, release criteria,
  and post-v0.1 direction. It is not a live feature checklist.
- [`graphics-direction.md`](./graphics-direction.md) — the implemented
  post-v0.1 living-diorama renderer, visual asset, performance, and observer
  boundaries.
- [`implementation-plan-v0.1.md`](./implementation-plan-v0.1.md) — the
  preserved technical plan and decision record, including milestone gates and
  candidate implementation notes. It is not a deployment runbook.

## Acceptance and compatibility evidence

- [`living-settlement-living2-acceptance.md`](./living-settlement-living2-acceptance.md)
  — local measured acceptance for `v02-rng1-living2`, including the 100-year
  seed-42 run and year-50 SQLite continuation comparison. It is not hosted CI,
  deployment, or a guarantee for every seed.
- [`living-settlement-acceptance.md`](./living-settlement-acceptance.md) — the
  preserved historical `v02-rng1-living1` results, including seed-42 extinction
  by year 72. These results remain specific to living1.
- [`pre-v0.2-stability-certification.md`](./pre-v0.2-stability-certification.md)
  — stabilization findings, fixes, deterministic and recovery evidence,
  deployment/browser checks, validation limits, and certification status.
- [`performance-pass-2026-09-19.md`](./performance-pass-2026-09-19.md) — local
  crash recovery, loading, movement, deployment, and validation evidence.
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
