import { describe, expect, it } from 'vitest'
import { parseLivingWorld } from './living'

const world = { version: 1, rulesVersion: 'v02-rng1-living1', worldMinute: 1440, age: 'Foraging', capabilities: ['work'], weather: 'Fair', temperature: 15, rainfall: 25, completedOrders: 0, foodHarvested: 0, foodPrepared: 0, careGiven: 0, goodsSpoiled: 0, totalFacts: 0, stock: [], people: [], orders: [], fields: [], facilities: [], animals: [], facts: [] }

describe('living world observations', () => {
  it('supports legacy observations and rejects unknown versions', () => {
    expect(parseLivingWorld(null)).toBeNull()
    expect(parseLivingWorld(world)?.weather).toBe('Fair')
    expect(() => parseLivingWorld({ ...world, version: 2 })).toThrow()
  })
  it('keeps large citizen identities as decimal strings', () => {
    const result = parseLivingWorld({ ...world, people: [{ citizenId: '9007199254740993', goal: 'FamilySecurity', mood: 6000, stress: 0, injury: 0, illness: 0, toolCondition: 0, clothingCondition: 0, knowledge: [], deathObserved: false, experiences: [] }] })
    expect(result?.people[0].citizenId).toBe('9007199254740993')
  })
  it('rejects invalid coordinates, quantities, and incomplete nested data', () => {
    expect(() => parseLivingWorld({ ...world, stock: [{ good: 'Grain', quantity: -1 }] })).toThrow()
    expect(() => parseLivingWorld({ ...world, animals: [{ id: '1', predator: false, location: { x: -1, y: 0 }, energy: 4000 }] })).toThrow()
    expect(() => parseLivingWorld({ ...world, people: [{}] })).toThrow()
  })
})
