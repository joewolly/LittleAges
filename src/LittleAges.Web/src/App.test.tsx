import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

const citizen = { citizenId: '9223372036854775807', name: 'Elara Venn', age: 18, lifeStage: 'Adult', location: { x: 1, y: 2 }, health: 10000, currentAction: 'Build', actionSequence: 0, isAlive: true, deathMinute: null, deathCause: null, hunger: 120, rest: 80, actionPhase: 'Perform', carriedResource: null, carriedQuantity: null, targetResourceNodeId: null, homeStructureId: '1', targetStructureId: '2', occupation: 'Builder', lifetimeWorkActivity: { foragingMinutes: 0, woodcuttingMinutes: 0, stoneworkingMinutes: 0, constructionMinutes: 16, haulingMinutes: 4 } }
const structure = { structureId: '2', type: 'Shelter', status: 'UnderConstruction', location: { x: 2, y: 1 }, startedMinute: 12, completedMinute: null, requiredWood: 10, deliveredWood: 3, requiredStone: 4, deliveredStone: 1, requiredWork: 20, completedWork: 7, condition: 0, capacity: 4, storageBonus: null, constructionMultiplierBasisPoints: null, currentOccupantIds: [], contributions: [{ citizenId: citizen.citizenId, constructionWork: 7, woodDelivered: 3, stoneDelivered: 1 }] }
const settlement = { foodStored: 400, woodStored: 120, stoneStored: 30, livingPopulation: 20, deadPopulation: 0, totalPopulation: 20, remainingResources: [], resources: [], storageCapacity: 1000, storageUsed: 550, shelterCapacity: 24, shelteredPopulation: 19, unhousedPopulation: 1, completedShelters: 5, completedStockpiles: 1, completedWorkshops: 0, exposureGraceUntilMinute: 720, activeConstructionProject: structure }
const map = { width: 3, height: 3, terrain: [1, 2, 3, 4, 5, 1, 2, 3, 4], startingSite: { x: 1, y: 1 } }
const household = { householdId: '3', createdMinute: 12, dissolvedMinute: null, dwellingStructureId: '1', memberIds: [citizen.citizenId], livingMemberIds: [citizen.citizenId], partnerPair: null, childrenIds: [] }
const historicalEvent = { eventId: '9223372036854775806', historicalEventId: '9223372036854775806', worldMinute: 12, eventType: 'CitizenBorn', importance: 'Personal', origin: 'Live', location: { x: 1, y: 2 }, payloadJson: `{"citizenId":"${citizen.citizenId}"}`, schemaVersion: 1, summary: 'Elara Venn was born.', citizenLinks: [{ eventId: '9223372036854775806', citizenId: citizen.citizenId, role: 'subject' }], structureLinks: [] }
const statisticsSample = { worldMinute: 43200, periodStartMinute: 0, population: 20, birthsPeriod: 2, deathsPeriod: 0, foodStored: 400, foodProducedPeriod: 100, foodConsumedPeriod: 80, woodStored: 120, stoneStored: 30, shelterCapacity: 24, averageHealth: 9800, averageHunger: 1200 }

function historyEvent(eventId: number, worldMinute = eventId) { return { ...historicalEvent, eventId: String(eventId), historicalEventId: String(eventId), worldMinute, summary: `Historical event ${eventId}`, citizenLinks: [{ ...historicalEvent.citizenLinks[0], eventId: String(eventId) }] } }
function statisticsAt(index: number) { return { ...statisticsSample, worldMinute: index * 43200, periodStartMinute: (index - 1) * 43200 } }

type Deferred<T> = { promise: Promise<T>; resolve: (value: T) => void }

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(nextResolve => { resolve = nextResolve })
  return { promise, resolve }
}

function responseFor(path: string, worldMinute: number, name = citizen.name) {
  if (path.endsWith('/map')) return new Response(JSON.stringify(map))
  if (path.endsWith('/health')) return new Response('Healthy')
  if (path.endsWith('/status')) return new Response(JSON.stringify({ state: 'Running', worldMinute, pendingEventCount: 1, worldSeed: '42', population: 20, totalPopulation: 20, livingPopulation: 20, deadPopulation: 0 }))
  if (path.endsWith('/settlement')) return new Response(JSON.stringify({ ...settlement, livingPopulation: 20, deadPopulation: 0, totalPopulation: 20 }))
  if (path.endsWith('/structures')) return new Response(JSON.stringify([structure]))
  if (path.endsWith('/households')) return new Response(JSON.stringify([]))
  if (path.startsWith('/api/v1/history')) return new Response(JSON.stringify([]))
  if (path.startsWith('/api/v1/statistics')) return new Response(JSON.stringify([]))
  return new Response(JSON.stringify([{ ...citizen, name }]))
}

afterEach(() => {
  cleanup()
  vi.useRealTimers()
  vi.restoreAllMocks()
})

beforeEach(() => {
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null)
})

describe('citizen observer', () => {
  it('shows an accessible loading state while requests are pending', () => {
    vi.spyOn(globalThis, 'fetch').mockReturnValue(new Promise<Response>(() => {}))
    render(<App />)
    expect(screen.getByText('Loading citizens…')).toBeInTheDocument()
  })

  it('renders the citizen list after successful polling', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const path = String(input)
      if (path.endsWith('/map')) return new Response(JSON.stringify(map))
      if (path.endsWith('/health')) return new Response('Healthy')
      if (path.endsWith('/status')) return new Response(JSON.stringify({ state: 'Running', worldMinute: 10, pendingEventCount: 1, worldSeed: '42', population: 20, totalPopulation: 20, livingPopulation: 20, deadPopulation: 0 }))
      if (path.endsWith('/settlement')) return new Response(JSON.stringify(settlement))
      if (path.endsWith('/structures')) return new Response(JSON.stringify([structure]))
      if (path.endsWith('/households')) return new Response(JSON.stringify([]))
      return new Response(JSON.stringify([citizen]))
    })
    render(<App />)
    expect(await screen.findByRole('heading', { name: 'Elara Venn' })).toBeInTheDocument()
    expect(screen.getByText('At (1, 2)')).toBeInTheDocument()
  })

  it('shows an accessible API error', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('offline'))
    render(<App />)
    expect((await screen.findAllByRole('alert'))[0]).toHaveTextContent('offline')
  })

  it('waits for a slow load before starting the next poll', async () => {
    vi.useFakeTimers()
    const paths: string[] = []
    const requests: Array<Deferred<Response>> = []
    vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      paths.push(String(input))
      const request = deferred<Response>()
      requests.push(request)
      return request.promise
    })

    render(<App />)
    expect(requests).toHaveLength(9)

    await act(async () => { await vi.advanceTimersByTimeAsync(2000) })
    expect(requests).toHaveLength(9)

    await act(async () => {
      requests.forEach((request, index) => request.resolve(responseFor(paths[index], 1)))
      await Promise.all(requests.map(request => request.promise))
    })
    await act(async () => { await vi.advanceTimersByTimeAsync(1999) })
    expect(requests).toHaveLength(9)
    await act(async () => { await vi.advanceTimersByTimeAsync(1) })
    expect(requests).toHaveLength(15)
    expect(paths).toContain('/api/v1/settlement')
    expect(paths.filter(path => path.endsWith('/map'))).toHaveLength(1)
  })

  it('applies each completed poll in order and continues polling', async () => {
    vi.useFakeTimers()
    let poll = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const path = String(input)
      if (path === '/api/v1/status') poll += 1
      const cycle = Math.max(poll, 1)
      return responseFor(String(input), cycle, `Citizen ${cycle}`)
    })

    render(<App />)
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(screen.getByRole('heading', { name: 'Citizen 1' })).toBeInTheDocument()
    expect(screen.getByText('1', { selector: 'strong' })).toBeInTheDocument()

    await act(async () => { await vi.advanceTimersByTimeAsync(2000) })
    expect(screen.getByRole('heading', { name: 'Citizen 2' })).toBeInTheDocument()
    expect(screen.getByText('2', { selector: 'strong' })).toBeInTheDocument()
    expect(poll).toBe(2)
  })

  it('does not poll or update state after unmount', async () => {
    vi.useFakeTimers()
    const paths: string[] = []
    const requests: Array<Deferred<Response>> = []
    vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      paths.push(String(input))
      const request = deferred<Response>()
      requests.push(request)
      return request.promise
    })

    const { unmount } = render(<App />)
    expect(requests).toHaveLength(9)
    unmount()

    await act(async () => {
      requests.forEach((request, index) => request.resolve(responseFor(paths[index], 1)))
      await Promise.all(requests.map(request => request.promise))
      await vi.advanceTimersByTimeAsync(10000)
    })
    expect(requests).toHaveLength(9)
  })

  it('shows settlement survival metrics and citizen survival details', async () => {
    const deadCitizen = { ...citizen, citizenId: '2', name: 'Bram Vale', health: 0, isAlive: false, currentAction: 'Dead', actionPhase: 'None', deathMinute: 360, deathCause: 'starvation', hunger: 10000, rest: 9200 }
    vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const path = String(input)
      if (path.endsWith('/map')) return new Response(JSON.stringify(map))
      if (path.endsWith('/health')) return new Response('Healthy')
      if (path.endsWith('/status')) return new Response(JSON.stringify({ state: 'Running', worldMinute: 360, pendingEventCount: 1, worldSeed: '42', population: 1, totalPopulation: 2, livingPopulation: 1, deadPopulation: 1 }))
      if (path.endsWith('/settlement')) return new Response(JSON.stringify({ ...settlement, livingPopulation: 1, deadPopulation: 1, totalPopulation: 2, shelteredPopulation: 1, unhousedPopulation: 0, foodStored: 7, woodStored: 8, stoneStored: 9 }))
      if (path.endsWith('/structures')) return new Response(JSON.stringify([structure]))
      if (path.endsWith('/households')) return new Response(JSON.stringify([]))
      return new Response(JSON.stringify([citizen, deadCitizen]))
    })
    render(<App />)
    expect(await screen.findByRole('heading', { name: 'Elara Venn' })).toBeInTheDocument()
    expect(screen.getByText('Storage')).toBeInTheDocument()
    expect(screen.getAllByText('Shelter').length).toBeGreaterThan(0)
    expect(screen.getByText('Buildings complete')).toBeInTheDocument()
    expect(screen.getByText('Exposure grace')).toBeInTheDocument()
    expect(screen.getByText('Food')).toBeInTheDocument()
    expect(screen.getAllByText('Wood').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Stone').length).toBeGreaterThan(0)
    expect(screen.getByText('Bram Vale')).toBeInTheDocument()
    expect(screen.getByText(/years · Adult · Alive/)).toBeInTheDocument()
    expect(screen.getByText(/years · Adult · Dead/)).toBeInTheDocument()
    expect(screen.getByText('starvation')).toBeInTheDocument()
    expect(screen.getByText('Perform')).toBeInTheDocument()
    expect(screen.getAllByText('Health')).toHaveLength(2)
    expect(screen.queryByText('Carrying')).not.toBeInTheDocument()
    expect(screen.queryByText('Target node')).not.toBeInTheDocument()
    expect(screen.getAllByText('120')).toHaveLength(1)
    expect(screen.getAllByText('80')).toHaveLength(1)
  })

  it('renders M4 construction, settlement, and accessible map observations', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async input => responseFor(String(input), 42))
    render(<App />)
    expect(await screen.findByText('Storage')).toBeInTheDocument()
    expect(screen.getByText('Shelter, stores, and workshop')).toBeInTheDocument()
    expect(screen.getByText('Build / haul target')).toBeInTheDocument()
    expect(screen.getByText('Structure 2')).toBeInTheDocument()
    expect(screen.getByRole('img', { name: /Settlement map, 3 by 3 tiles/i })).toBeInTheDocument()
    expect(screen.getByText('Starting site')).toBeInTheDocument()
  })

  it('renders selected read-only relationship and household details without polling either endpoint', async () => {
    const socialCitizen = { ...citizen, currentAction: 'Socialize', shelter: 30, social: 80, parentAId: '2', parentBId: '3', partnerId: '4', householdId: household.householdId, childrenIds: ['5'], targetCitizenId: '6' }
    const relationship = { otherCitizenId: '4', otherCitizenName: 'Bram Vale', familiarity: 3100, affinity: 2200, trust: 1800, conflict: 25, lastInteractionMinute: 720, interactionCount: 3, label: 'Friend' }
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const path = String(input)
      if (path.endsWith('/map')) return new Response(JSON.stringify(map))
      if (path.endsWith('/health')) return new Response('Healthy')
      if (path.endsWith('/status')) return new Response(JSON.stringify({ state: 'Running', worldMinute: 720, pendingEventCount: 1, worldSeed: '42' }))
      if (path.endsWith('/settlement')) return new Response(JSON.stringify({ ...settlement, householdCount: 1, activeHouseholdCount: 1, partnershipCount: 1, relationshipCount: 1, friendCount: 1, rivalCount: 0, youngChildCount: 0, childCount: 0, adolescentCount: 0, adultCount: 20, elderCount: 0 }))
      if (path.endsWith('/structures')) return new Response(JSON.stringify([structure]))
      if (path.endsWith('/households/3')) return new Response(JSON.stringify(household))
      if (path.endsWith('/households')) return new Response(JSON.stringify([household]))
      if (path.endsWith('/relationships')) return new Response(JSON.stringify([relationship]))
      return new Response(JSON.stringify([socialCitizen]))
    })
    render(<App />)
    expect(await screen.findByRole('heading', { name: 'Elara Venn' })).toBeInTheDocument()
    expect(screen.getByText('Households')).toBeInTheDocument()
    expect(screen.getByText('Socialize')).toBeInTheDocument()
    expect(screen.getByText('Citizen 6')).toBeInTheDocument()
    expect(screen.getByText('Demographics')).toBeInTheDocument()
    expect(screen.getByText('Family')).toBeInTheDocument()
    expect(fetchMock.mock.calls.filter(([path]) => String(path).endsWith('/relationships'))).toHaveLength(0)
    fireEvent.click(screen.getByRole('button', { name: 'View relationship details' }))
    expect(await screen.findByRole('heading', { name: 'Relationships for Elara Venn' })).toBeInTheDocument()
    expect(screen.getByText('Bram Vale')).toBeInTheDocument()
    expect(screen.getByText('Affinity')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'View household details' }))
    expect(await screen.findByRole('heading', { name: 'Household 3' })).toBeInTheDocument()
    expect(screen.getByText('Dissolved')).toBeInTheDocument()
    expect(fetchMock.mock.calls.filter(([path]) => String(path).endsWith('/relationships'))).toHaveLength(1)
    expect(fetchMock.mock.calls.filter(([path]) => String(path).endsWith('/households/3'))).toHaveLength(1)
    expect(fetchMock.mock.calls.every(([, init]) => init === undefined || init.method === undefined || init.method === 'GET')).toBe(true)
    expect(screen.getByText('No controls. No interruptions.')).toBeInTheDocument()
  })

  it('renders historical events and statistics, opens a biography, and never sends mutation requests', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const path = String(input)
      if (path.startsWith('/api/v1/history')) return new Response(JSON.stringify([historicalEvent]))
      if (path.startsWith('/api/v1/statistics')) return new Response(JSON.stringify([statisticsSample]))
      if (path.includes('/biography')) return new Response(JSON.stringify({ citizen, events: [historicalEvent], memories: [{ citizenId: citizen.citizenId, eventId: historicalEvent.eventId, memoryType: 'ChildBorn', importance: 'Personal', emotionalValence: 900, createdMinute: 12 }], parentIds: [], partnerId: null, childrenIds: [], birthMinute: 12, deathMinute: null, deathCause: null }))
      return responseFor(path, 12)
    })
    render(<App />)
    expect(await screen.findByText('Elara Venn was born.')).toBeInTheDocument()
    expect(screen.getByText('Year 0 · Spring · Month 1, Day 1 · 00:12')).toBeInTheDocument()
    expect(screen.getByLabelText('Population trend')).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: '43,200' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'View biography' }))
    expect(await screen.findByRole('heading', { name: 'Biography' })).toBeInTheDocument()
    expect(screen.getByText('Child Born')).toBeInTheDocument()
    expect(fetchMock.mock.calls.every(([, init]) => init === undefined || init.method === undefined || init.method === 'GET')).toBe(true)
  })

  it('constructs history filters from the observer controls', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async input => responseFor(String(input), 12))
    render(<App />)
    expect(await screen.findByRole('heading', { name: 'Elara Venn' })).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Minimum importance'), { target: { value: '4' } })
    fireEvent.change(screen.getByLabelText('Event type'), { target: { value: 'CitizenDied' } })
    fireEvent.change(screen.getByLabelText('Citizen ID'), { target: { value: '7' } })
    fireEvent.change(screen.getByLabelText('Family root ID'), { target: { value: '8' } })
    fireEvent.change(screen.getByLabelText('Structure ID'), { target: { value: '9' } })
    fireEvent.change(screen.getByLabelText('From minute'), { target: { value: '10' } })
    fireEvent.change(screen.getByLabelText('To minute'), { target: { value: '20' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    const historyPaths = fetchMock.mock.calls.map(([input]) => String(input)).filter(path => path.startsWith('/api/v1/history'))
    expect(historyPaths.at(-1)).toBe('/api/v1/history?fromMinute=10&toMinute=20&eventType=CitizenDied&minimumImportance=4&citizenId=7&familyCitizenId=8&structureId=9&limit=50')
  })

  it('continues history pagination while each returned page is full', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const path = String(input)
      if (path.startsWith('/api/v1/history?')) {
        const cursor = new URL(path, 'http://localhost').searchParams.get('beforeEventId')
        const page = cursor === null ? Array.from({ length: 50 }, (_, index) => historyEvent(300 - index)) : cursor === '251' ? Array.from({ length: 50 }, (_, index) => historyEvent(250 - index)) : cursor === '201' ? Array.from({ length: 50 }, (_, index) => historyEvent(200 - index)) : [historyEvent(150)]
        return new Response(JSON.stringify(page))
      }
      return responseFor(path, 12)
    })
    render(<App />)
    expect(await screen.findByRole('button', { name: 'Load older events' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Load older events' }))
    await waitFor(() => expect(fetchMock.mock.calls.map(([input]) => String(input)).filter(path => path.startsWith('/api/v1/history?'))).toHaveLength(2))
    expect(await screen.findByText('Historical event 201')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Load older events' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Load older events' }))
    await waitFor(() => expect(fetchMock.mock.calls.map(([input]) => String(input)).filter(path => path.startsWith('/api/v1/history?'))).toHaveLength(3))
    expect(await screen.findByText('Historical event 151')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Load older events' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Load older events' }))
    await waitFor(() => expect(fetchMock.mock.calls.map(([input]) => String(input)).filter(path => path.startsWith('/api/v1/history?'))).toHaveLength(4))
    expect(await screen.findByText('Historical event 150')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Load older events' })).not.toBeInTheDocument())
    expect(document.querySelectorAll('.history-event')).toHaveLength(151)
    const historyPaths = fetchMock.mock.calls.map(([input]) => String(input)).filter(path => path.startsWith('/api/v1/history?'))
    expect(historyPaths[1]).toContain('beforeEventId=251')
    expect(historyPaths[2]).toContain('beforeEventId=201')
    expect(historyPaths[3]).toContain('beforeEventId=151')
  })

  it('navigates through bounded statistics pages beyond the oldest 100 samples', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const path = String(input)
      if (path.startsWith('/api/v1/statistics?')) {
        const fromMinute = new URL(path, 'http://localhost').searchParams.get('fromMinute')
        return new Response(JSON.stringify(fromMinute === null ? Array.from({ length: 100 }, (_, index) => statisticsAt(index + 1)) : [statisticsAt(101)]))
      }
      return responseFor(path, 12)
    })
    render(<App />)
    expect(await screen.findByRole('button', { name: 'Load later samples' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Load later samples' }))
    await waitFor(() => expect(screen.getByRole('cell', { name: '4,363,200' })).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'Load later samples' })).not.toBeInTheDocument()
    const statisticsPaths = fetchMock.mock.calls.map(([input]) => String(input)).filter(path => path.startsWith('/api/v1/statistics?'))
    expect(statisticsPaths).toHaveLength(2)
    expect(statisticsPaths[1]).toBe('/api/v1/statistics?fromMinute=4320001&limit=100')
  })
})
