export type Farm = { structureId: string; year: number; stage: string; plantingWork: number; tendingWork: number; yield: number; remaining: number; harvested: number }
export type Harvest = { structureId: string; year: number; yield: number; harvested: number; lost: number }
export type Agriculture = { enabled: true; version: number; food: number; dedicatedFoodCapacity: number; winterReserveTarget: number; projectedCoverageDays: number; farms: Farm[]; harvests: Harvest[]; totalFarms: number; totalHarvests: number; offset: number; limit: number } | { enabled: false }
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const count = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
const id = (value: unknown) => typeof value === 'string' && /^[1-9]\d*$/.test(value) && BigInt(value) <= 9223372036854775807n
export function parseAgriculture(value: unknown): Agriculture {
  if (!record(value) || typeof value.enabled !== 'boolean') throw new Error('Invalid farming information.')
  if (!value.enabled) return { enabled: false }
  if (value.version !== 1 || ['food', 'dedicatedFoodCapacity', 'winterReserveTarget', 'projectedCoverageDays', 'totalFarms', 'totalHarvests', 'offset', 'limit'].some(k => !count(value[k])) ||
      !Array.isArray(value.farms) || !Array.isArray(value.harvests) || value.farms.length > 200 || value.harvests.length > 200) throw new Error('Invalid farming totals.')
  for (const farm of value.farms) if (!record(farm) || !id(farm.structureId) || !['Fallow', 'Planted', 'Growing', 'Harvest', 'Dormant'].includes(String(farm.stage)) ||
    ['year', 'plantingWork', 'tendingWork', 'yield', 'remaining', 'harvested'].some(k => !count(farm[k]))) throw new Error('Invalid crop information.')
  for (const harvest of value.harvests) if (!record(harvest) || !id(harvest.structureId) || ['year', 'yield', 'harvested', 'lost'].some(k => !count(harvest[k])) ||
    Number(harvest.harvested) + Number(harvest.lost) !== harvest.yield) throw new Error('Invalid harvest information.')
  return value as Agriculture
}
