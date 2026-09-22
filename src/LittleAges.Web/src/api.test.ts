import { describe, expect, it, vi } from 'vitest'
import { buildHistoryQuery, buildStatisticsQuery, changeSimulationSpeed, DEFAULT_OPERATIONAL_SPEED, fetchBiography, fetchHistory, fetchStatistics, OPERATIONAL_SPEED_OPTIONS, parseBiography, parseCitizens, parseHealth, parseHistory, parseHistoricalEvent, parseHousehold, parseHouseholds, parseMap, parseRelationships, parseSettlement, parseStatistics, parseStatus, parseStructures, pauseSimulation, resumeSimulation } from './api'

const citizen = {
  citizenId: '9223372036854775807',
  name: 'Elara Venn',
  age: 18,
  lifeStage: 'Adult',
  location: { x: 1, y: 2 },
  health: 10000,
  currentAction: 'Idle',
  actionSequence: 0,
  actionStartedMinute: { value: 10 },
  actionCompletesMinute: { value: 20 },
  target: { x: 2, y: 2 },
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
  founderOrdinal: null,
  parentAId: null,
  parentBId: null,
  partnerId: null,
  householdId: null,
  childrenIds: [],
  targetCitizenId: null,
}

const relationship = { otherCitizenId: '9223372036854775807', otherCitizenName: 'Bram Vale', familiarity: 3000, affinity: -25, trust: 2200, conflict: 100, lastInteractionMinute: 720, interactionCount: 4, label: 'Friend' }
const household = { householdId: '9223372036854775807', createdMinute: 720, dissolvedMinute: null, dwellingStructureId: '7', memberIds: ['2', '3'], livingMemberIds: ['2', '3'], partnerPair: ['2', '3'], childrenIds: [] }

const structure = {
  structureId: '9223372036854775807', type: 'Shelter', status: 'UnderConstruction', location: { x: 1, y: 2 }, startedMinute: 20, completedMinute: null,
  requiredWood: 12, deliveredWood: 5, requiredStone: 4, deliveredStone: 2, requiredWork: 30, completedWork: 9, condition: 0,
  capacity: 4, storageBonus: null, constructionMultiplierBasisPoints: null, currentOccupantIds: [], contributions: [{ citizenId: citizen.citizenId, constructionWork: 9, woodDelivered: 5, stoneDelivered: 2 }],
}

function historicalEvent(eventId = '9223372036854775806', worldMinute = 720) {
  return {
    eventId,
    historicalEventId: eventId,
    worldMinute,
    eventType: 'CitizenBorn',
    importance: 'Personal',
    origin: 'Live',
    location: { x: 1, y: 2 },
    payloadJson: `{"citizenId":"${citizen.citizenId}"}`,
    schemaVersion: 1,
    summary: 'Elara Venn was born.',
    citizenLinks: [{ eventId, citizenId: citizen.citizenId, role: 'subject' }],
    structureLinks: [],
  }
}

const statisticsSample = {
  worldMinute: 43200,
  periodStartMinute: 0,
  population: 25,
  birthsPeriod: 5,
  deathsPeriod: 1,
  foodStored: 400,
  foodProducedPeriod: 100,
  foodConsumedPeriod: 80,
  woodStored: 120,
  stoneStored: 30,
  shelterCapacity: 24,
  averageHealth: 9800,
  averageHunger: 1200,
}

describe('API response parsing', () => {
  it('accepts a stable departure before the observation and keeps old movement payloads compatible', () => {
    const movementPlan = { actionSequence: 4, observedMinute: 104, segmentStartedMinute: 100, waypoints: [{ x: 1, y: 2, arriveMinute: 104 }, { x: 2, y: 2, arriveMinute: 110 }] }
    const moving = { ...citizen, currentAction: 'Wander', actionPhase: 'TravelToTarget', actionSequence: 4, movementPlan }
    expect(parseCitizens([moving])[0].movementPlan?.segmentStartedMinute).toBe(100)
    expect(parseCitizens([{ ...moving, movementPlan: { ...movementPlan, segmentStartedMinute: undefined } }])[0].movementPlan?.segmentStartedMinute).toBeUndefined()
  })
  it.each([-1, 104.5, 105, 110, Number.NaN])('rejects invalid or future movement departure %s', segmentStartedMinute => {
    const movementPlan = { actionSequence: 4, observedMinute: 104, segmentStartedMinute, waypoints: [{ x: 1, y: 2, arriveMinute: 104 }, { x: 2, y: 2, arriveMinute: 110 }] }
    expect(() => parseCitizens([{ ...citizen, currentAction: 'Wander', actionPhase: 'TravelToTarget', actionSequence: 4, movementPlan }])).toThrow()
  })
  it('accepts plain text health responses', () => expect(parseHealth('Healthy')).toEqual({ ok: true, label: 'Healthy' }))
  it('keeps status safe when fields are missing or malformed', () => expect(parseStatus({ worldMinute: 'later', state: 4 })).toEqual({ state: 'Unknown', worldMinute: null, pendingEventCount: null, worldSeed: null, error: null, paused: false, operationalSpeed: null }))
  it('parses immutable operational controls from status', () => expect(parseStatus({ state: 'Running', paused: true, operationalSpeed: 5 })).toMatchObject({ paused: true, operationalSpeed: 5 }))
  it('defines normal pace as one 1440-minute day per 1000 real seconds', () => {
    expect(DEFAULT_OPERATIONAL_SPEED).toBe(1.44)
    expect(OPERATIONAL_SPEED_OPTIONS.map(option => option.value)).toEqual([1.44, 4.32, 8.64])
  })
  it('submits bounded operational control requests with strict methods and bodies', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => new Response(JSON.stringify({ state: 'Running', paused: false, operationalSpeed: 5 })))
    await pauseSimulation()
    await resumeSimulation()
    await changeSimulationSpeed(DEFAULT_OPERATIONAL_SPEED)
    expect(fetchMock.mock.calls[0]).toEqual(['/api/v1/control/pause', { method: 'POST' }])
    expect(fetchMock.mock.calls[1]).toEqual(['/api/v1/control/resume', { method: 'POST' }])
    expect(fetchMock.mock.calls[2][0]).toBe('/api/v1/control/speed')
    expect(fetchMock.mock.calls[2][1]).toMatchObject({ method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{"speed":1.44}' })
    await expect(changeSimulationSpeed(0)).rejects.toThrow()
    await expect(changeSimulationSpeed(1001)).rejects.toThrow()
  })
  it('preserves the full decimal world seed without numeric coercion', () => {
    const status = parseStatus({ worldSeed: '18446744073709551615' })
    expect(status.worldSeed).toBe('18446744073709551615')
  })
  it('preserves positive decimal citizen IDs losslessly', () => {
    expect(parseCitizens([citizen])[0]).toMatchObject({ citizenId: citizen.citizenId, movementPlan: null })
  })

  it('strictly parses authoritative movement plans while accepting older responses without them', () => {
    const moving = { ...citizen, currentAction: 'Wander', actionPhase: 'TravelToTarget', actionSequence: 4, target: { x: 3, y: 3 }, movementPlan: { actionSequence: 4, observedMinute: 100, waypoints: [{ x: 1, y: 2, arriveMinute: 100 }, { x: 2, y: 2, arriveMinute: 110 }, { x: 3, y: 3, arriveMinute: 124 }] } }
    expect(parseCitizens([moving])[0].movementPlan).toEqual(moving.movementPlan)
    expect(parseCitizens([citizen])[0].movementPlan).toBeNull()
  })

  it.each([
    { actionSequence: 3, observedMinute: 100, waypoints: [{ x: 1, y: 2, arriveMinute: 100 }, { x: 2, y: 2, arriveMinute: 110 }] },
    { actionSequence: 4, observedMinute: 100, waypoints: [{ x: 1, y: 2, arriveMinute: 101 }, { x: 2, y: 2, arriveMinute: 110 }] },
    { actionSequence: 4, observedMinute: 100, waypoints: [{ x: 1, y: 2, arriveMinute: 100 }, { x: 3, y: 2, arriveMinute: 110 }] },
    { actionSequence: 4, observedMinute: 100, waypoints: [{ x: 1, y: 2, arriveMinute: 100 }, { x: 2, y: 2, arriveMinute: 100 }] },
  ])('rejects an incoherent movement plan', movementPlan => expect(() => parseCitizens([{ ...citizen, currentAction: 'Wander', actionPhase: 'TravelToTarget', actionSequence: 4, target: { x: 2, y: 2 }, movementPlan }])).toThrow())
  it('rejects malformed citizen IDs', () => {
    expect(() => parseCitizens([{ citizenId: '01' }])).toThrow()
  })

  it.each(['None', 'Idle', 'Rest', 'Wander', 'Explore', 'Eat', 'GatherFood', 'GatherWood', 'GatherStone', 'Dead', 'HaulConstruction', 'Build', 'Socialize'])('accepts CitizenAction %s from ASP.NET JSON', currentAction => {
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

  it('parses M5 social and nullable family fields without losing decimal IDs', () => {
    const parsed = parseCitizens([{ ...citizen, founderOrdinal: 4, parentAId: '2', parentBId: '3', partnerId: '4', householdId: '5', childrenIds: ['6', '9007199254740993'], targetCitizenId: '7', shelter: 300, social: 400, currentAction: 'Socialize' }])[0]
    expect(parsed).toMatchObject({ founderOrdinal: 4, parentAId: '2', parentBId: '3', partnerId: '4', householdId: '5', childrenIds: ['6', '9007199254740993'], targetCitizenId: '7', shelter: 300, social: 400, currentAction: 'Socialize' })
  })

  it('parses stable relationship and household snapshots', () => {
    expect(parseRelationships([relationship])[0]).toEqual(relationship)
    expect(parseHousehold(household)).toEqual(household)
    expect(parseHouseholds([household])[0].householdId).toBe(household.householdId)
  })

  it.each([
    () => parseRelationships([{ ...relationship, otherCitizenId: '01' }]),
    () => parseRelationships([relationship, { ...relationship, otherCitizenId: '2' }]),
    () => parseRelationships([{ ...relationship, affinity: -10001 }]),
    () => parseHousehold({ ...household, partnerPair: ['2'] }),
    () => parseHousehold({ ...household, livingMemberIds: ['4'] }),
    () => parseHouseholds([household, { ...household, householdId: '2' }]),
  ])('rejects malformed or unstable M5 social snapshots', parse => expect(parse).toThrow())

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

  it('parses M5 social settlement counters', () => {
    const parsed = parseSettlement({ foodStored: 4, woodStored: 5, stoneStored: 6, livingPopulation: 3, deadPopulation: 0, totalPopulation: 3, householdCount: 2, activeHouseholdCount: 1, partnershipCount: 1, relationshipCount: 2, friendCount: 1, rivalCount: 1, youngChildCount: 0, childCount: 0, adolescentCount: 0, adultCount: 3, elderCount: 0 })
    expect(parsed).toMatchObject({ householdCount: 2, activeHouseholdCount: 1, partnershipCount: 1, relationshipCount: 2, friendCount: 1, rivalCount: 1, adultCount: 3 })
  })

  it('parses canonical structures and a row-major terrain map', () => {
    expect(parseStructures([structure])[0].contributions[0].constructionWork).toBe(9)
    expect(parseMap({ width: 2, height: 2, terrain: [1, 2, 3, 4], startingSite: { x: 1, y: 1 } })).toEqual({ width: 2, height: 2, terrain: [1, 2, 3, 4], elevation: [0, 0, 0, 0], resources: [], startingSite: { x: 1, y: 1 } })
    expect(parseMap({ width: 2, height: 2, terrain: [1, 2, 3, 4], elevation: [100, 200, 300, 400], resources: [{ resourceNodeId: '7', resourceType: 'Wood', location: { x: 1, y: 0 }, maximumQuantity: 500, regenerationPotential: 80 }], startingSite: { x: 1, y: 1 } })).toMatchObject({ elevation: [100, 200, 300, 400], resources: [{ resourceNodeId: '7', resourceType: 'Wood' }] })
    expect(parseCitizens([citizen])[0]).toMatchObject({ actionStartedMinute: 10, actionCompletesMinute: 20, target: { x: 2, y: 2 } })
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
    () => parseMap({ width: 2, height: 2, terrain: [1, 2, 3, 4], elevation: [0, 0, 0], startingSite: { x: 0, y: 0 } }),
    () => parseMap({ width: 2, height: 2, terrain: [1, 2, 3, 4], resources: [{ resourceNodeId: '1', resourceType: 'Wood', location: { x: 2, y: 0 }, maximumQuantity: 1, regenerationPotential: 0 }], startingSite: { x: 0, y: 0 } }),
    () => parseCitizens([{ ...citizen, actionStartedMinute: { value: 21 }, actionCompletesMinute: { value: 20 } }]),
    () => parseCitizens([{ ...citizen, actionStartedMinute: { value: 10, unit: 'minute' } }]),
  ])('rejects malformed M4 observation data', parse => expect(parse).toThrow())

  it.each([
    { foodStored: '400', woodStored: 120, stoneStored: 30, livingPopulation: 20, deadPopulation: 0 },
    { foodStored: 400, woodStored: 120, stoneStored: 30, livingPopulation: 20, deadPopulation: 0, totalPopulation: 19 },
    { foodStored: 400, woodStored: 120, stoneStored: 30, livingPopulation: 20, deadPopulation: 0, resources: [{ resourceNodeId: '01', resourceType: 'Food', currentQuantity: 1 }] },
  ])('rejects malformed settlement fields', value => {
    expect(() => parseSettlement(value)).toThrow()
  })

  it('parses canonical historical events while preserving decimal IDs as strings', () => {
    const parsed = parseHistoricalEvent(historicalEvent())
    expect(parsed.eventId).toBe('9223372036854775806')
    expect(parsed.citizenLinks[0].citizenId).toBe(citizen.citizenId)
    expect(parsed.origin).toBe('Live')
  })

  it.each([
    { eventId: 2 },
    { historicalEventId: '7' },
    { eventType: 'Unknown' },
    { importance: 'Historic-ish' },
    { origin: 'Imported' },
    { schemaVersion: 2 },
    { payloadJson: '{broken' },
    { location: { x: -1, y: 1 } },
    { citizenLinks: [{ eventId: '9223372036854775806', citizenId: citizen.citizenId, role: 'Subject' }] },
  ])('rejects malformed historical event DTOs', change => {
    expect(() => parseHistoricalEvent({ ...historicalEvent(), ...change })).toThrow()
  })

  it('enforces descending event history and ascending biography timelines', () => {
    const newest = historicalEvent('10', 720)
    const older = historicalEvent('9', 720)
    expect(parseHistory([newest, older]).map(event => event.eventId)).toEqual(['10', '9'])
    expect(() => parseHistory([older, newest])).toThrow()

    const biography = parseBiography({
      citizen,
      events: [historicalEvent('9', 10), historicalEvent('10', 20)],
      memories: [],
      parentIds: [],
      partnerId: null,
      childrenIds: [],
      birthMinute: 10,
      deathMinute: null,
      deathCause: null,
    })
    expect(biography.events.map(event => event.worldMinute)).toEqual([10, 20])
  })

  it('parses biography relationships and citizen-owned structured memories', () => {
    const biography = parseBiography({
      citizen,
      events: [],
      memories: [{ citizenId: citizen.citizenId, eventId: '9223372036854775806', memoryType: 'FriendshipFormed', importance: 'Notable', emotionalValence: 1200, createdMinute: 720 }],
      parentIds: ['2', '3'],
      partnerId: '4',
      childrenIds: ['5'],
      birthMinute: 0,
      deathMinute: 100,
      deathCause: 'old age',
    })
    expect(biography.parentIds).toEqual(['2', '3'])
    expect(biography.memories[0]).toMatchObject({ memoryType: 'FriendshipFormed', emotionalValence: 1200 })
    expect(() => parseBiography({ citizen, events: [], memories: [{ citizenId: '2', eventId: '7', memoryType: 'FriendshipFormed', importance: 'Notable', emotionalValence: 0, createdMinute: 1 }], parentIds: [], childrenIds: [] })).toThrow()
  })

  it('accepts safe signed biography birth minutes for pre-world founders', () => {
    const biography = parseBiography({ citizen, events: [], memories: [], parentIds: [], partnerId: null, childrenIds: [], birthMinute: -1_000, deathMinute: null, deathCause: null })
    expect(biography.birthMinute).toBe(-1_000)
  })

  it('parses bounded ascending historical statistics', () => {
    expect(parseStatistics([statisticsSample])[0]).toMatchObject({ worldMinute: 43200, population: 25, averageHealth: 9800 })
    expect(() => parseStatistics([{ ...statisticsSample, averageHunger: 10001 }])).toThrow()
    expect(() => parseStatistics([statisticsSample, { ...statisticsSample, worldMinute: 43200 }])).toThrow()
  })

  it('constructs bounded history and statistics filters with the M6 defaults', () => {
    const defaults = new URLSearchParams(buildHistoryQuery())
    expect(defaults.get('minimumImportance')).toBe('2')
    expect(defaults.get('limit')).toBe('50')

    const history = new URLSearchParams(buildHistoryQuery({ fromMinute: 10, toMinute: 20, eventType: 'CitizenDied', minimumImportance: 4, citizenId: citizen.citizenId, familyCitizenId: '2', structureId: '7', beforeEventId: '8', limit: 25 }))
    expect(history.get('fromMinute')).toBe('10')
    expect(history.get('toMinute')).toBe('20')
    expect(history.get('eventType')).toBe('CitizenDied')
    expect(history.get('minimumImportance')).toBe('4')
    expect(history.get('citizenId')).toBe(citizen.citizenId)
    expect(history.get('familyCitizenId')).toBe('2')
    expect(history.get('structureId')).toBe('7')
    expect(history.get('beforeEventId')).toBe('8')
    expect(history.get('limit')).toBe('25')
    expect(new URLSearchParams(buildStatisticsQuery({ fromMinute: 0, toMinute: 43200, limit: 100 })).get('limit')).toBe('100')
  })

  it.each([
    () => buildHistoryQuery({ limit: 101 }),
    () => buildHistoryQuery({ minimumImportance: 6 }),
    () => buildHistoryQuery({ citizenId: '01' }),
    () => buildHistoryQuery({ fromMinute: 20, toMinute: 10 }),
    () => buildStatisticsQuery({ limit: 0 }),
  ])('rejects invalid history query bounds', build => expect(build).toThrow())

  it('uses GET history, biography, and statistics fetchers with strict response parsers', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const path = String(input)
      if (path.startsWith('/api/v1/history?')) return new Response(JSON.stringify([historicalEvent()]))
      if (path.includes('/biography')) return new Response(JSON.stringify({ citizen, events: [], memories: [], parentIds: [], partnerId: null, childrenIds: [], birthMinute: 0, deathMinute: null, deathCause: null }))
      return new Response(JSON.stringify([statisticsSample]))
    })
    await fetchHistory({ limit: 1 })
    await fetchBiography(citizen.citizenId)
    await fetchStatistics({ limit: 1 })
    expect(fetchMock.mock.calls.map(([input]) => String(input))).toEqual([
      `/api/v1/history?minimumImportance=2&limit=1`,
      `/api/v1/citizens/${citizen.citizenId}/biography`,
      '/api/v1/statistics?limit=1',
    ])
    expect(fetchMock.mock.calls.every(([, init]) => init === undefined)).toBe(true)
    fetchMock.mockRestore()
  })
})
