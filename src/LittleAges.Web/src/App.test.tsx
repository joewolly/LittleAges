import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

const liveMock = vi.hoisted(() => {
  type WorldChangedHandler = (message: unknown) => void
  type WorldFrameHandler = (message: unknown) => void
  type ConnectionHandler = () => void
  const state = {
    startMode: 'reject' as 'resolve' | 'reject',
    startPromise: null as Promise<void> | null,
    startCalls: 0,
    stopCalls: 0,
    emitWorldChanged: (message: unknown) => { void message },
    emitWorldFrame: (message: unknown) => { void message },
    emitStreamError: () => undefined,
    emitReconnecting: () => undefined,
    emitReconnected: () => undefined,
    emitClose: () => undefined,
  }
  const factory = vi.fn(() => {
    const worldChangedHandlers: WorldChangedHandler[] = []
    const worldFrameHandlers: WorldFrameHandler[] = []
    const streamErrorHandlers: ConnectionHandler[] = []
    const reconnectingHandlers: ConnectionHandler[] = []
    const reconnectedHandlers: ConnectionHandler[] = []
    const closeHandlers: ConnectionHandler[] = []
    state.emitWorldChanged = message => { worldChangedHandlers.forEach(handler => handler(message)) }
    state.emitWorldFrame = message => { worldFrameHandlers.forEach(handler => handler(message)) }
    state.emitStreamError = () => { streamErrorHandlers.forEach(handler => handler()) }
    state.emitReconnecting = () => { reconnectingHandlers.forEach(handler => handler()) }
    state.emitReconnected = () => { reconnectedHandlers.forEach(handler => handler()) }
    state.emitClose = () => { closeHandlers.forEach(handler => handler()) }
    return {
      start: vi.fn(async () => { state.startCalls += 1; if (state.startMode === 'reject') throw new Error('SignalR unavailable'); if (state.startPromise !== null) await state.startPromise }),
      stop: vi.fn(async () => { state.stopCalls += 1 }),
      onWorldChanged: (handler: WorldChangedHandler) => { worldChangedHandlers.push(handler) },
      onWorldFrame: (handler: WorldFrameHandler) => { worldFrameHandlers.push(handler) },
      onStreamError: (handler: ConnectionHandler) => { streamErrorHandlers.push(handler) },
      onReconnecting: (handler: ConnectionHandler) => { reconnectingHandlers.push(handler) },
      onReconnected: (handler: ConnectionHandler) => { reconnectedHandlers.push(handler) },
      onClose: (handler: ConnectionHandler) => { closeHandlers.push(handler) },
    }
  })
  return { state, factory }
})

vi.mock('./live', () => ({ createWorldConnection: liveMock.factory }))

const citizen = {
  citizenId: '9223372036854775807',
  name: 'Elara Venn',
  age: 18,
  lifeStage: 'Adult',
  location: { x: 1, y: 2 },
  health: 10000,
  currentAction: 'Build',
  actionSequence: 0,
  actionStartedMinute: null,
  actionCompletesMinute: null,
  actionPhase: 'Perform',
  target: null,
  isAlive: true,
  deathMinute: null,
  deathCause: null,
  hunger: 120,
  rest: 80,
  shelter: 0,
  social: 0,
  carriedResource: null,
  carriedQuantity: null,
  targetResourceNodeId: null,
  homeStructureId: '1',
  targetStructureId: '2',
  targetCitizenId: null,
  occupation: 'Builder',
  founderOrdinal: null,
  parentAId: null,
  parentBId: null,
  partnerId: null,
  householdId: null,
  childrenIds: [],
  movementPlan: null,
  lifetimeWorkActivity: { foragingMinutes: 0, woodcuttingMinutes: 0, stoneworkingMinutes: 0, constructionMinutes: 16, haulingMinutes: 4 },
}
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

function renderAppWithRecords(tab: 'Overview' | 'Citizens' | 'Buildings' | 'History' | 'Statistics' = 'Citizens') {
  const result = render(<App />)
  fireEvent.click(screen.getByRole('button', { name: 'Observer records' }))
  if (tab !== 'Overview') fireEvent.click(screen.getByRole('tab', { name: tab }))
  return result
}

afterEach(() => {
  cleanup()
  vi.useRealTimers()
  vi.restoreAllMocks()
})

beforeEach(() => {
  liveMock.state.startMode = 'reject'
  liveMock.state.startPromise = null
  liveMock.state.startCalls = 0
  liveMock.state.stopCalls = 0
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null)
})

describe('citizen observer', () => {
  it('shows an accessible loading state while requests are pending', () => {
    vi.spyOn(globalThis, 'fetch').mockReturnValue(new Promise<Response>(() => {}))
    render(<App />)
    expect(screen.getByText('Preparing the living diorama…')).toBeInTheDocument()
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
    renderAppWithRecords()
    fireEvent.click(await screen.findByRole('listitem', { name: /Elara Venn/ }))
    expect(await screen.findByRole('heading', { name: 'Elara Venn' })).toBeInTheDocument()
    expect(screen.getByText('At (1, 2)')).toBeInTheDocument()
  })

  it('renders operational controls and sends bounded control requests', async () => {
    let paused = false
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const path = String(input)
      if (path.endsWith('/control/pause')) paused = true
      if (path.endsWith('/control/resume')) paused = false
      if (path.endsWith('/control/speed')) return new Response(JSON.stringify({ state: 'Running', worldMinute: 12, pendingEventCount: 1, worldSeed: '42', paused, operationalSpeed: 5 }))
      if (path.endsWith('/control/pause') || path.endsWith('/control/resume')) return new Response(JSON.stringify({ state: 'Running', worldMinute: 12, pendingEventCount: 1, worldSeed: '42', paused, operationalSpeed: 10 }))
      return responseFor(path, 12)
    })
    renderAppWithRecords()
    fireEvent.click(await screen.findByRole('listitem', { name: /Elara Venn/ }))
    expect(await screen.findByRole('heading', { name: 'Elara Venn' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Set the pace' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Pause' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Resume' })).not.toBeDisabled())
    fireEvent.click(screen.getByRole('button', { name: 'Resume' }))
    fireEvent.change(screen.getByLabelText('Simulation speed'), { target: { value: '5' } })
    await waitFor(() => expect(fetchMock.mock.calls.filter(([, request]) => request?.method === 'POST')).toHaveLength(3))
    expect(fetchMock.mock.calls.some(([path]) => String(path).endsWith('/control/pause'))).toBe(true)
    expect(fetchMock.mock.calls.some(([path, request]) => String(path).endsWith('/control/speed') && request?.method === 'POST')).toBe(true)
  })

  it('shows an operational control error without hiding observer data', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async input => String(input).endsWith('/control/pause') ? new Response('unavailable', { status: 503 }) : responseFor(String(input), 12))
    renderAppWithRecords()
    expect(await screen.findByRole('heading', { name: 'Elara Venn' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Pause' }))
    expect(await screen.findByText('Request failed (503)')).toBeInTheDocument()
  })

  it('shows an accessible API error', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('offline'))
    renderAppWithRecords()
    expect((await screen.findAllByRole('alert'))[0]).toHaveTextContent('offline')
  })

  it('focuses the records panel and restores focus when Escape closes it', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async input => responseFor(String(input), 12))
    renderAppWithRecords('Overview')
    const close = screen.getByRole('button', { name: 'Close observer records' })
    await waitFor(() => expect(close).toHaveFocus())
    expect(screen.queryByRole('heading', { name: 'Lives in motion' })).not.toBeInTheDocument()
    fireEvent.keyDown(window, { key: 'Escape' })
    const reopen = screen.getByRole('button', { name: 'Observer records' })
    await waitFor(() => expect(reopen).toHaveFocus())
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

    renderAppWithRecords()
    expect(requests).toHaveLength(7)

    await act(async () => { await vi.advanceTimersByTimeAsync(2000) })
    expect(requests).toHaveLength(7)

    await act(async () => {
      requests.forEach((request, index) => request.resolve(responseFor(paths[index], 1)))
      await Promise.all(requests.map(request => request.promise))
    })
    await act(async () => { await vi.advanceTimersByTimeAsync(1999) })
    expect(requests).toHaveLength(7)
    await act(async () => { await vi.advanceTimersByTimeAsync(1) })
    await act(async () => { await Promise.resolve() })
    expect(requests.length).toBeGreaterThan(7)
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

    renderAppWithRecords()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(screen.getByRole('heading', { name: 'Citizen 1' })).toBeInTheDocument()
    expect(screen.getByText(/Current:.*00:01/)).toBeInTheDocument()

    await act(async () => { await vi.advanceTimersByTimeAsync(2000) })
    expect(screen.getByRole('heading', { name: 'Citizen 2' })).toBeInTheDocument()
    expect(screen.getByText(/Current:.*00:02/)).toBeInTheDocument()
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
    expect(requests).toHaveLength(7)
    unmount()

    await act(async () => {
      requests.forEach((request, index) => request.resolve(responseFor(paths[index], 1)))
      await Promise.all(requests.map(request => request.promise))
      await vi.advanceTimersByTimeAsync(10000)
    })
    expect(requests).toHaveLength(7)
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
    renderAppWithRecords('Overview')
    expect(await screen.findByText('Storage')).toBeInTheDocument()
    expect(screen.getByText('Storage')).toBeInTheDocument()
    expect(screen.getAllByText('Shelter').length).toBeGreaterThan(0)
    expect(screen.getByText('Buildings complete')).toBeInTheDocument()
    expect(screen.getByText('Exposure grace')).toBeInTheDocument()
    expect(screen.getByText('Food')).toBeInTheDocument()
    expect(screen.getAllByText('Wood').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Stone').length).toBeGreaterThan(0)
    fireEvent.click(screen.getByRole('tab', { name: 'Citizens' }))
    expect(await screen.findByRole('heading', { name: 'Elara Venn' })).toBeInTheDocument()
    expect(screen.getByText('Bram Vale')).toBeInTheDocument()
    expect(screen.getByText(/years · Adult · Alive/)).toBeInTheDocument()
    expect(screen.getByText('Perform')).toBeInTheDocument()
    fireEvent.click(screen.getByText('Bram Vale').closest('button')!)
    expect(await screen.findByText(/years · Adult · Dead/)).toBeInTheDocument()
    expect(screen.getByText('starvation')).toBeInTheDocument()
    expect(screen.getAllByText('Health')).toHaveLength(1)
    expect(screen.queryByText('Carrying')).not.toBeInTheDocument()
    expect(screen.queryByText('Target node')).not.toBeInTheDocument()
    expect(screen.getAllByText('10,000')).toHaveLength(1)
    expect(screen.getAllByText('9,200')).toHaveLength(1)
  })

  it('renders M4 construction, settlement, and accessible map observations', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async input => responseFor(String(input), 42))
    renderAppWithRecords('Overview')
    expect(await screen.findByText('Storage')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('tab', { name: 'Buildings' }))
    expect(screen.getByText('Shelter, stores, and workshop')).toBeInTheDocument()
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
    renderAppWithRecords()
    expect(await screen.findByRole('heading', { name: 'Elara Venn' })).toBeInTheDocument()
    expect(screen.getByText('Households')).toBeInTheDocument()
    expect(screen.getAllByText('Socialize').length).toBeGreaterThan(0)
    expect(screen.getByText('Citizen 6')).toBeInTheDocument()
    expect(screen.getByText('Demographics')).toBeInTheDocument()
    expect(screen.getByText('Family')).toBeInTheDocument()
    expect(fetchMock.mock.calls.filter(([path]) => String(path).endsWith('/relationships'))).toHaveLength(0)
    fireEvent.click(screen.getByRole('button', { name: 'View relationship details' }))
    expect(await screen.findByRole('heading', { name: 'Relationships for Elara Venn' })).toBeInTheDocument()
    expect(screen.getByText('Bram Vale')).toBeInTheDocument()
    expect(screen.getByText('Affinity')).toBeInTheDocument()
    fireEvent.click(screen.getAllByRole('button', { name: 'View household details' }).at(-1)!)
    expect(await screen.findByRole('heading', { name: 'Household 3' })).toBeInTheDocument()
    expect(screen.getByText('Dissolved')).toBeInTheDocument()
    expect(fetchMock.mock.calls.filter(([path]) => String(path).endsWith('/relationships'))).toHaveLength(1)
    expect(fetchMock.mock.calls.filter(([path]) => String(path).endsWith('/households/3'))).toHaveLength(1)
    expect(fetchMock.mock.calls.every(([, init]) => init === undefined || init.method === undefined || init.method === 'GET')).toBe(true)
    expect(screen.getByText('Controls affect operational pacing only.')).toBeInTheDocument()
  })

  it('renders historical events and statistics, opens a biography, and never sends mutation requests', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const path = String(input)
      if (path.startsWith('/api/v1/history')) return new Response(JSON.stringify([historicalEvent]))
      if (path.startsWith('/api/v1/statistics')) return new Response(JSON.stringify([statisticsSample]))
      if (path.includes('/biography')) return new Response(JSON.stringify({ citizen, events: [historicalEvent], memories: [{ citizenId: citizen.citizenId, eventId: historicalEvent.eventId, memoryType: 'ChildBorn', importance: 'Personal', emotionalValence: 900, createdMinute: 12 }], parentIds: [], partnerId: null, childrenIds: [], birthMinute: 12, deathMinute: null, deathCause: null }))
      return responseFor(path, 12)
    })
    renderAppWithRecords('History')
    expect(await screen.findByText('Elara Venn was born.')).toBeInTheDocument()
    expect(screen.getByText('Year 0 · Spring · Month 1, Day 1 · 00:12')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('tab', { name: 'Statistics' }))
    expect(await screen.findByLabelText('Population trend')).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: '43,200' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('tab', { name: 'Citizens' }))
    fireEvent.click(screen.getByRole('button', { name: 'View biography' }))
    expect(await screen.findByRole('heading', { name: 'Biography' })).toBeInTheDocument()
    expect(screen.getByText('Child Born')).toBeInTheDocument()
    expect(fetchMock.mock.calls.every(([, init]) => init === undefined || init.method === undefined || init.method === 'GET')).toBe(true)
  })

  it('constructs history filters from the observer controls', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async input => responseFor(String(input), 12))
    renderAppWithRecords('History')
    expect(await screen.findByLabelText('Minimum importance')).toBeInTheDocument()
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
    renderAppWithRecords('History')
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
    renderAppWithRecords('Statistics')
    expect(await screen.findByRole('button', { name: 'Load later samples' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Load later samples' }))
    await waitFor(() => expect(screen.getByRole('cell', { name: '4,363,200' })).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'Load later samples' })).not.toBeInTheDocument()
    const statisticsPaths = fetchMock.mock.calls.map(([input]) => String(input)).filter(path => path.startsWith('/api/v1/statistics?'))
    expect(statisticsPaths).toHaveLength(2)
    expect(statisticsPaths[1]).toBe('/api/v1/statistics?fromMinute=4320001&limit=100')
  })

  it('applies connected live frames without replacing the scene through REST', async () => {
    liveMock.state.startMode = 'resolve'
    let statusCalls = 0
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const path = String(input)
      if (path.endsWith('/status')) statusCalls += 1
      return responseFor(path, statusCalls || 1, `Citizen ${statusCalls || 1}`)
    })
    renderAppWithRecords()
    expect(await screen.findByRole('heading', { name: 'Citizen 1' })).toBeInTheDocument()
    await waitFor(() => expect(screen.getByText('Live updates: connected')).toBeInTheDocument())
    expect(statusCalls).toBe(1)

    liveMock.state.emitWorldFrame({ sequence: 1, sentAtUnixMilliseconds: 1000, revision: 2, status: { state: 'Running', worldMinute: 2, pendingEventCount: 1, worldSeed: '42', paused: false, operationalSpeed: 10 }, citizens: [{ ...citizen, name: 'Streamed citizen' }], structures: [structure] })
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Streamed citizen' })).toBeInTheDocument())
    expect(screen.getByText(/Current:.*00:02/)).toBeInTheDocument()
    expect(statusCalls).toBe(1)

    liveMock.state.emitWorldChanged({ revision: 3, worldMinute: 3, hostState: 'Running', persistenceState: 'Ready' })
    expect(statusCalls).toBe(1)
    expect(fetchMock.mock.calls.filter(([input]) => String(input).startsWith('/api/v1/history?'))).toHaveLength(0)

    liveMock.state.emitReconnecting()
    await waitFor(() => expect(screen.getByText('Live updates: reconnecting…')).toBeInTheDocument())
    liveMock.state.emitReconnected()
    await waitFor(() => expect(screen.getByText('Live updates: connected')).toBeInTheDocument())
    expect(statusCalls).toBe(1)
    expect(screen.getByText('Live updates: connected')).toBeInTheDocument()
  })

  it('accepts the first streamed frame after a slow SignalR startup without restarting REST scene polling', async () => {
    vi.useFakeTimers()
    liveMock.state.startMode = 'resolve'
    const start = deferred<void>()
    liveMock.state.startPromise = start.promise
    let worldMinute = 1
    let statusCalls = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const path = String(input)
      if (path.endsWith('/status')) statusCalls += 1
      return responseFor(path, worldMinute, `Citizen ${worldMinute}`)
    })

    renderAppWithRecords()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(screen.getByRole('heading', { name: 'Citizen 1' })).toBeInTheDocument()
    expect(liveMock.state.startCalls).toBe(1)

    worldMinute = 2
    await act(async () => {
      start.resolve()
      await start.promise
      await vi.advanceTimersByTimeAsync(0)
    })

    liveMock.state.emitWorldFrame({ sequence: 1, sentAtUnixMilliseconds: 1000, revision: 2, status: { state: 'Running', worldMinute: 2, pendingEventCount: 1, worldSeed: '42', paused: false, operationalSpeed: 10 }, citizens: [{ ...citizen, name: 'Citizen 2' }], structures: [structure] })
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(screen.getByRole('heading', { name: 'Citizen 2' })).toBeInTheDocument()
    expect(screen.getByText(/Current:.*00:02/)).toBeInTheDocument()
    expect(statusCalls).toBe(1)
    await act(async () => { await vi.advanceTimersByTimeAsync(9999) })
    expect(statusCalls).toBe(1)
  })

  it('uses a visible degraded state and two-second visible REST fallback when SignalR is unavailable', async () => {
    vi.useFakeTimers()
    let statusCalls = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const path = String(input)
      if (path.endsWith('/status')) statusCalls += 1
      return responseFor(path, statusCalls || 1)
    })
    render(<App />)
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(screen.getByText('Live updates: unavailable')).toBeInTheDocument()
    expect(statusCalls).toBe(1)
    await act(async () => { await vi.advanceTimersByTimeAsync(1999) })
    expect(statusCalls).toBe(1)
    await act(async () => { await vi.advanceTimersByTimeAsync(1) })
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(statusCalls).toBe(2)
  })

  it('keeps a newer reconnect biography when the older response resolves later', async () => {
    liveMock.state.startMode = 'resolve'
    const biographyRequests: Array<Deferred<Response>> = []
    vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const path = String(input)
      if (path.includes('/biography')) {
        const request = deferred<Response>()
        biographyRequests.push(request)
        return request.promise
      }
      return responseFor(path, 12)
    })
    renderAppWithRecords()
    expect(await screen.findByRole('heading', { name: 'Elara Venn' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'View biography' }))
    await waitFor(() => expect(biographyRequests).toHaveLength(1))

    liveMock.state.emitReconnected()
    await waitFor(() => expect(biographyRequests).toHaveLength(2))
    const biographyFor = (name: string) => new Response(JSON.stringify({ citizen: { ...citizen, name }, events: [], memories: [], parentIds: [], partnerId: null, childrenIds: [], birthMinute: 0, deathMinute: null, deathCause: null }))
    await act(async () => { biographyRequests[1].resolve(biographyFor('New name')); await biographyRequests[1].promise })
    expect(await screen.findByRole('heading', { name: 'New name' })).toBeInTheDocument()
    await act(async () => { biographyRequests[0].resolve(biographyFor('Old name')); await biographyRequests[0].promise })
    expect(screen.getByRole('heading', { name: 'New name' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Old name' })).not.toBeInTheDocument()
  })
})
