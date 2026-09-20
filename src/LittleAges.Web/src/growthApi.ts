export type GrowthObservation = {
  living: number; births: number; deaths: number; unpartneredAdults: number; unhoused: number
  foodReserveTarget: number; deathCauses: Record<string, number>
  households: { householdId: string; ready: boolean; blockers: string[] }[]
}

export function parseGrowth(value: unknown): GrowthObservation {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) throw new Error('Population information is unavailable.')
  const data = value as Record<string, unknown>
  for (const key of ['living', 'births', 'deaths', 'unpartneredAdults', 'unhoused', 'foodReserveTarget']) {
    if (!Number.isSafeInteger(data[key]) || (data[key] as number) < 0) throw new Error('Invalid population count.')
  }
  if (typeof data.deathCauses !== 'object' || data.deathCauses === null || Array.isArray(data.deathCauses) ||
    Object.values(data.deathCauses).some(count => !Number.isSafeInteger(count) || count < 0)) throw new Error('Invalid mortality counts.')
  if (!Array.isArray(data.households) || data.households.some(h => typeof h !== 'object' || h === null ||
    typeof h.householdId !== 'string' || !/^[1-9]\d*$/.test(h.householdId) || typeof h.ready !== 'boolean' ||
    !Array.isArray(h.blockers) || h.blockers.some((b: unknown) => typeof b !== 'string'))) throw new Error('Invalid household outlook.')
  if (new Set(data.households.map(h => h.householdId)).size !== data.households.length) throw new Error('Duplicate household outlook.')
  return data as GrowthObservation
}

