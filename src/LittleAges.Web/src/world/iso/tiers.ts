import type { Citizen, Structure } from '../../api'
import type { LivingWorld } from '../../living'

export type ShelterTier = 1 | 2 | 3 | 4 | 5

/** Lifetime minutes of woodcutting and of stoneworking that mark a settlement's materials as mature.
 * Seed 42 under m15 rules reaches the timber mark around year 5 and the masonry mark around year 20. */
export const TIMBER_MINUTES = 150_000
export const MASONRY_MINUTES = 400_000

/**
 * The look of every Shelter, read from facts the simulation already records.
 * Every input only grows (techniques are kept, finished buildings stay, lifetime
 * work accumulates), so a settlement never visibly slips back a tier.
 *
 * 1 Hide tent       at founding
 * 2 Round hut       Cultivation is known
 * 3 Wattle cottage  Toolmaking is known and a Workshop is finished
 * 4 Timber longhouse  plus a lifetime of woodcutting
 * 5 Stone house     plus a lifetime of stoneworking
 *
 * Worlds without living-settlement rules have no techniques, so a finished
 * Workshop stands in for both techniques there.
 */
export function shelterTier(living: LivingWorld | null | undefined, structures: readonly Structure[], citizens: readonly Citizen[]): ShelterTier {
  const known = new Set<string>()
  for (const person of living?.people ?? []) if (!person.deathObserved) for (const technique of person.knowledge) known.add(technique)
  const workshop = structures.some(structure => structure.type === 'Workshop' && structure.status === 'Complete')
  const hasTechniques = living !== null && living !== undefined
  const cultivation = hasTechniques ? known.has('Cultivation') : workshop
  const toolmaking = hasTechniques ? known.has('Toolmaking') : workshop
  if (!cultivation) return 1
  if (!toolmaking || !workshop) return 2
  const woodcutting = citizens.reduce((total, citizen) => total + citizen.lifetimeWorkActivity.woodcuttingMinutes, 0)
  if (woodcutting < TIMBER_MINUTES) return 3
  const stoneworking = citizens.reduce((total, citizen) => total + citizen.lifetimeWorkActivity.stoneworkingMinutes, 0)
  return stoneworking < MASONRY_MINUTES ? 4 : 5
}
