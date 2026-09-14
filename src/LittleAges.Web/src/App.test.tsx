import { act, cleanup, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

const citizen = { citizenId: '9223372036854775807', name: 'Elara Venn', age: 18, lifeStage: 'Adult', location: { x: 1, y: 2 }, health: 10000, currentAction: 'Build', actionSequence: 0, isAlive: true, deathMinute: null, deathCause: null, hunger: 120, rest: 80, actionPhase: 'Perform', carriedResource: null, carriedQuantity: null, targetResourceNodeId: null, homeStructureId: '1', targetStructureId: '2', occupation: 'Builder', lifetimeWorkActivity: { foragingMinutes: 0, woodcuttingMinutes: 0, stoneworkingMinutes: 0, constructionMinutes: 16, haulingMinutes: 4 } }
const structure = { structureId: '2', type: 'Shelter', status: 'UnderConstruction', location: { x: 2, y: 1 }, startedMinute: 12, completedMinute: null, requiredWood: 10, deliveredWood: 3, requiredStone: 4, deliveredStone: 1, requiredWork: 20, completedWork: 7, condition: 0, capacity: 4, storageBonus: null, constructionMultiplierBasisPoints: null, currentOccupantIds: [], contributions: [{ citizenId: citizen.citizenId, constructionWork: 7, woodDelivered: 3, stoneDelivered: 1 }] }
const settlement = { foodStored: 400, woodStored: 120, stoneStored: 30, livingPopulation: 20, deadPopulation: 0, totalPopulation: 20, remainingResources: [], resources: [], storageCapacity: 1000, storageUsed: 550, shelterCapacity: 24, shelteredPopulation: 19, unhousedPopulation: 1, completedShelters: 5, completedStockpiles: 1, completedWorkshops: 0, exposureGraceUntilMinute: 720, activeConstructionProject: structure }
const map = { width: 3, height: 3, terrain: [1, 2, 3, 4, 5, 1, 2, 3, 4], startingSite: { x: 1, y: 1 } }

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
    expect(requests).toHaveLength(6)

    await act(async () => { await vi.advanceTimersByTimeAsync(2000) })
    expect(requests).toHaveLength(6)

    await act(async () => {
      requests.forEach((request, index) => request.resolve(responseFor(paths[index], 1)))
      await Promise.all(requests.map(request => request.promise))
    })
    await act(async () => { await vi.advanceTimersByTimeAsync(1999) })
    expect(requests).toHaveLength(6)
    await act(async () => { await vi.advanceTimersByTimeAsync(1) })
    expect(requests).toHaveLength(11)
    expect(paths).toContain('/api/v1/settlement')
    expect(paths.filter(path => path.endsWith('/map'))).toHaveLength(1)
  })

  it('applies each completed poll in order and continues polling', async () => {
    vi.useFakeTimers()
    let callCount = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const cycle = callCount === 0 ? 1 : Math.floor((callCount - 1) / 5) + 1
      callCount += 1
      return responseFor(String(input), cycle, `Citizen ${cycle}`)
    })

    render(<App />)
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(screen.getByRole('heading', { name: 'Citizen 1' })).toBeInTheDocument()
    expect(screen.getByText('1', { selector: 'strong' })).toBeInTheDocument()

    await act(async () => { await vi.advanceTimersByTimeAsync(2000) })
    expect(screen.getByRole('heading', { name: 'Citizen 2' })).toBeInTheDocument()
    expect(screen.getByText('2', { selector: 'strong' })).toBeInTheDocument()
    expect(callCount).toBe(11)
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
    expect(requests).toHaveLength(6)
    unmount()

    await act(async () => {
      requests.forEach((request, index) => request.resolve(responseFor(paths[index], 1)))
      await Promise.all(requests.map(request => request.promise))
      await vi.advanceTimersByTimeAsync(10000)
    })
    expect(requests).toHaveLength(6)
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
    expect(screen.getByText(/years · Alive/)).toBeInTheDocument()
    expect(screen.getByText(/years · Dead/)).toBeInTheDocument()
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
})
