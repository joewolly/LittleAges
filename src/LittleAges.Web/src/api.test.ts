import { describe, expect, it } from 'vitest'
import { parseCitizens, parseHealth, parseMap, parseSettlement, parseStatus, parseStructures } from './api'

const citizen = {
  citizenId: '9223372036854775807',
  name: 'Elara Venn',
  age: 18,
  lifeStage: 'Adult',
  location: { x: 1, y: 2 },
  health: 10000,
  currentAction: 'Idle',
  actionSequence: 0,
  isAlive: true,
  deathMinute: null,
  deathCause: null,
  hunger: 100,
  rest: 200,
  actionPhase: 'Perform',
  carriedResource: null,
  carriedQuantity: null,
  targetResourceNodeId: null,
  homeStructureId: null,
  targetStructureId: null,
  occupation: 'Builder',
  lifetimeWorkActivity: { foragingMinutes: 1, woodcuttingMinutes: 2, stoneworkingMinutes: 3, constructionMinutes: 4, haulingMinutes: 5 },
}

const structure = {
  structureId: '9223372036854775807', type: 'Shelter', status: 'UnderConstruction', location: { x: 1, y: 2 }, startedMinute: 20, completedMinute: null,
  requiredWood: 12, deliveredWood: 5, requiredStone: 4, deliveredStone: 2, requiredWork: 30, completedWork: 9, condition: 0,
  capacity: 4, storageBonus: null, constructionMultiplierBasisPoints: null, currentOccupantIds: [], contributions: [{ citizenId: citizen.citizenId, constructionWork: 9, woodDelivered: 5, stoneDelivered: 2 }],
}

describe('API response parsing', () => {
  it('accepts plain text health responses', () => expect(parseHealth('Healthy')).toEqual({ ok: true, label: 'Healthy' }))
  it('keeps status safe when fields are missing or malformed', () => expect(parseStatus({ worldMinute: 'later', state: 4 })).toEqual({ state: 'Unknown', worldMinute: null, pendingEventCount: null, worldSeed: null, error: null }))
  it('preserves the full decimal world seed without numeric coercion', () => {
    const status = parseStatus({ worldSeed: '18446744073709551615' })
    expect(status.worldSeed).toBe('18446744073709551615')
  })
  it('preserves positive decimal citizen IDs losslessly', () => {
    expect(parseCitizens([citizen])[0].citizenId).toBe(citizen.citizenId)
  })
  it('rejects malformed citizen IDs', () => {
    expect(() => parseCitizens([{ citizenId: '01' }])).toThrow()
  })

  it.each(['None', 'Idle', 'Rest', 'Wander', 'Explore', 'Eat', 'GatherFood', 'GatherWood', 'GatherStone', 'Dead'])('accepts CitizenAction %s from ASP.NET JSON', currentAction => {
    expect(parseCitizens([{ ...citizen, currentAction }])[0].currentAction).toBe(currentAction)
  })

  it.each(['None', 'TravelToTarget', 'Perform', 'ReturnToStockpile'])('accepts CitizenActionPhase %s from ASP.NET JSON', actionPhase => {
    expect(parseCitizens([{ ...citizen, actionPhase }])[0].actionPhase).toBe(actionPhase)
  })

  it('accepts nullable death, carry, and target fields', () => {
    const parsed = parseCitizens([{ ...citizen, isAlive: false, deathMinute: null, deathCause: null, carriedResource: null, carriedQuantity: null, targetResourceNodeId: null }])[0]
    expect(parsed.deathMinute).toBeNull()
    expect(parsed.deathCause).toBeNull()
    expect(parsed.carriedResource).toBeNull()
    expect(parsed.carriedQuantity).toBeNull()
    expect(parsed.targetResourceNodeId).toBeNull()
  })

  it('preserves resource node IDs losslessly and parses survival metrics', () => {
    const parsed = parseCitizens([{ ...citizen, hunger: 9000, rest: 8000, carriedResource: 'Stone', carriedQuantity: 3, targetResourceNodeId: '9007199254740993', isAlive: false, deathMinute: 360, deathCause: 'starvation', currentAction: 'Dead', actionPhase: 'None' }])[0]
    expect(parsed.targetResourceNodeId).toBe('9007199254740993')
    expect(parsed.hunger).toBe(9000)
    expect(parsed.rest).toBe(8000)
    expect(parsed.carriedResource).toBe('Stone')
    expect(parsed.carriedQuantity).toBe(3)
  })

  it('parses citizen settlement assignments and lifetime work activity', () => {
    const parsed = parseCitizens([citizen])[0]
    expect(parsed.occupation).toBe('Builder')
    expect(parsed.lifetimeWorkActivity).toEqual({ foragingMinutes: 1, woodcuttingMinutes: 2, stoneworkingMinutes: 3, constructionMinutes: 4, haulingMinutes: 5 })
  })

  it.each([
    ['health', { health: 'unwell' }],
    ['alive state', { isAlive: 'yes' }],
    ['death minute', { deathMinute: '360' }],
    ['death cause', { deathCause: 3 }],
    ['hunger', { hunger: 10001 }],
    ['rest', { rest: -1 }],
    ['action', { currentAction: 'Unknown' }],
    ['phase', { actionPhase: 'Unknown' }],
    ['carried resource', { carriedResource: 'Gold' }],
    ['carried quantity', { carriedQuantity: -1 }],
    ['resource target ID', { targetResourceNodeId: '01' }],
  ])('rejects malformed citizen %s fields', (_field, change) => {
    expect(() => parseCitizens([{ ...citizen, ...change }])).toThrow()
  })

  it('parses the settlement stockpile and aggregate resource fields', () => {
    const parsed = parseSettlement({
      foodStored: 400,
      woodStored: 120,
      stoneStored: 30,
      livingPopulation: 19,
      deadPopulation: 1,
      totalPopulation: 20,
      remainingResources: [{ resourceType: 'Food', quantity: 77 }],
      resources: [{ resourceNodeId: '9223372036854775807', resourceType: 'Stone', currentQuantity: 12 }],
    })
    expect(parsed.foodStored).toBe(400)
    expect(parsed.livingPopulation).toBe(19)
    expect(parsed.deadPopulation).toBe(1)
    expect(parsed.totalPopulation).toBe(20)
    expect(parsed.remainingResources[0]).toEqual({ resourceType: 'Food', quantity: 77 })
    expect(parsed.resources[0].resourceNodeId).toBe('9223372036854775807')
  })

  it('parses settlement construction aggregates and its active project', () => {
    const parsed = parseSettlement({ foodStored: 4, woodStored: 5, stoneStored: 6, livingPopulation: 3, deadPopulation: 0, totalPopulation: 3, storageCapacity: 100, storageUsed: 15, shelterCapacity: 4, shelteredPopulation: 2, unhousedPopulation: 1, completedShelters: 0, completedStockpiles: 0, completedWorkshops: 0, exposureGraceUntilMinute: 720, activeConstructionProject: structure })
    expect(parsed.storageUsed).toBe(15)
    expect(parsed.activeConstructionProject?.structureId).toBe(structure.structureId)
  })

  it('parses canonical structures and a row-major terrain map', () => {
    expect(parseStructures([structure])[0].contributions[0].constructionWork).toBe(9)
    expect(parseMap({ width: 2, height: 2, terrain: [1, 2, 3, 4], startingSite: { x: 1, y: 1 } })).toEqual({ width: 2, height: 2, terrain: [1, 2, 3, 4], startingSite: { x: 1, y: 1 } })
  })

  it.each([
    () => parseCitizens([{ ...citizen, homeStructureId: '01' }]),
    () => parseCitizens([{ ...citizen, occupation: 'Architect' }]),
    () => parseCitizens([{ ...citizen, lifetimeWorkActivity: { ...citizen.lifetimeWorkActivity, haulingMinutes: -1 } }]),
    () => parseStructures([{ ...structure, deliveredWood: 13 }]),
    () => parseStructures([{ ...structure, status: 'Complete', completedMinute: null }]),
    () => parseStructures([structure, { ...structure, structureId: '2' }]),
    () => parseMap({ width: 2, height: 2, terrain: [1, 2, 3], startingSite: { x: 0, y: 0 } }),
    () => parseMap({ width: 2, height: 2, terrain: [1, 2, 3, 9], startingSite: { x: 0, y: 0 } }),
  ])('rejects malformed M4 observation data', parse => expect(parse).toThrow())

  it.each([
    { foodStored: '400', woodStored: 120, stoneStored: 30, livingPopulation: 20, deadPopulation: 0 },
    { foodStored: 400, woodStored: 120, stoneStored: 30, livingPopulation: 20, deadPopulation: 0, totalPopulation: 19 },
    { foodStored: 400, woodStored: 120, stoneStored: 30, livingPopulation: 20, deadPopulation: 0, resources: [{ resourceNodeId: '01', resourceType: 'Food', currentQuantity: 1 }] },
  ])('rejects malformed settlement fields', value => {
    expect(() => parseSettlement(value)).toThrow()
  })
})
