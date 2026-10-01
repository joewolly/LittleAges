import { describe, expect, it } from 'vitest'
import { festivalStatus, livingWorkStage, parseLivingWorld } from './living'

const world = { version: 1, rulesVersion: 'v02-rng1-living1', worldMinute: 1440, age: 'Foraging', capabilities: ['work'], weather: 'Fair', temperature: 15, rainfall: 25, completedOrders: 0, foodHarvested: 0, foodPrepared: 0, careGiven: 0, goodsSpoiled: 0, totalFacts: 0, stock: [], people: [], orders: [], fields: [], facilities: [], animals: [], facts: [] }

describe('living world observations', () => {
  it('accepts legacy Living and unified M13/M14 rules while rejecting unknown versions', () => {
    expect(parseLivingWorld(null)).toBeNull()
    expect(parseLivingWorld(world)?.weather).toBe('Fair')
    expect(parseLivingWorld({ ...world, rulesVersion: 'v02-rng1-living2' })?.rulesVersion).toBe('v02-rng1-living2')
    expect(parseLivingWorld({ ...world, rulesVersion: 'm13-rng1-unified1' })?.rulesVersion).toBe('m13-rng1-unified1')
    expect(parseLivingWorld({ ...world, rulesVersion: 'm14-rng1-migration1' })?.rulesVersion).toBe('m14-rng1-migration1')
    expect(parseLivingWorld({ ...world, rulesVersion: 'm15-rng1-roads1' })?.rulesVersion).toBe('m15-rng1-roads1')
    expect(() => parseLivingWorld({ ...world, version: 2 })).toThrow()
    expect(() => parseLivingWorld({ ...world, rulesVersion: 'v02-rng1-living3' })).toThrow()
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


describe('festival observations', () => {
  const festival = { settlementId: '1', year: 0, location: { x: 12, y: 9 }, startMinute: 346320, endMinute: 346680, started: true, finished: false, mode: 'Feast', initialFood: 20, reservedFood: 10, consumedFood: 10, attendance: [{ citizenId: '9007199254740993', minutes: 60, benefitsGranted: true, portionConsumed: true }] }
  it('accepts M16 attendance and renders factual phase and activity labels', () => {
    const parsed = parseLivingWorld({ ...world, rulesVersion: 'm16-rng1-festivals1', capabilities: ['festivals'], festivals: [festival] })!
    expect(parsed.festivals?.[0].attendance[0].citizenId).toBe('9007199254740993')
    expect(festivalStatus(parsed.festivals![0])).toBe('Harvest feast underway')
    expect(festivalStatus({ ...parsed.festivals![0], finished: true, reservedFood: 0 })).toBe('Harvest festival remembered')
    expect(festivalStatus({ ...parsed.festivals![0], started: false, finished: true, mode: 'Gathering', initialFood: 0, reservedFood: 0, consumedFood: 0, attendance: [] })).toBe('No gathering this year')
    expect(livingWorkStage({ kind: 'AttendFestival', phase: 'Travel' } as Parameters<typeof livingWorkStage>[0])).toBe('Going to the festival')
    expect(livingWorkStage({ kind: 'AttendFestival', phase: 'Work' } as Parameters<typeof livingWorkStage>[0])).toBe('Celebrating')
  })
  it('rejects unknown modes, impossible attendance and unreturned food at closing', () => {
    for (const invalid of [{ ...festival, mode: 'Unknown' }, { ...festival, finished: true }, { ...festival, attendance: [{ ...festival.attendance[0], minutes: 361 }] }])
      expect(() => parseLivingWorld({ ...world, rulesVersion: 'm16-rng1-festivals1', festivals: [invalid] })).toThrow()
  })
})
