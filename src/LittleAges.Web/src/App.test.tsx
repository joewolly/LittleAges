import { act, cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

const citizen = { citizenId: '9223372036854775807', name: 'Elara Venn', age: 18, lifeStage: 'Adult', location: { x: 1, y: 2 }, health: 10000, currentAction: 'Idle', actionSequence: 0, isAlive: true, deathMinute: null, deathCause: null, hunger: 120, rest: 80, actionPhase: 'Perform', carriedResource: null, carriedQuantity: null, targetResourceNodeId: null }
const settlement = { foodStored: 400, woodStored: 120, stoneStored: 30, livingPopulation: 20, deadPopulation: 0, totalPopulation: 20, remainingResources: [], resources: [] }

type Deferred<T> = { promise: Promise<T>; resolve: (value: T) => void }

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(nextResolve => { resolve = nextResolve })
  return { promise, resolve }
}

function responseFor(path: string, worldMinute: number, name = citizen.name) {
  if (path.endsWith('/health')) return new Response('Healthy')
  if (path.endsWith('/status')) return new Response(JSON.stringify({ state: 'Running', worldMinute, pendingEventCount: 1, worldSeed: '42', population: 20, totalPopulation: 20, livingPopulation: 20, deadPopulation: 0 }))
  if (path.endsWith('/settlement')) return new Response(JSON.stringify({ ...settlement, livingPopulation: 20, deadPopulation: 0, totalPopulation: 20 }))
  return new Response(JSON.stringify([{ ...citizen, name }]))
}

afterEach(() => {
  cleanup()
  vi.useRealTimers()
  vi.restoreAllMocks()
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
      if (path.endsWith('/health')) return new Response('Healthy')
      if (path.endsWith('/status')) return new Response(JSON.stringify({ state: 'Running', worldMinute: 10, pendingEventCount: 1, worldSeed: '42', population: 20, totalPopulation: 20, livingPopulation: 20, deadPopulation: 0 }))
      if (path.endsWith('/settlement')) return new Response(JSON.stringify(settlement))
      return new Response(JSON.stringify([citizen]))
    })
    render(<App />)
    expect(await screen.findByRole('heading', { name: 'Elara Venn' })).toBeInTheDocument()
    expect(screen.getByText('At (1, 2)')).toBeInTheDocument()
  })

  it('shows an accessible API error', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('offline'))
    render(<App />)
    expect(await screen.findByRole('alert')).toHaveTextContent('offline')
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
    expect(requests).toHaveLength(4)

    await act(async () => { await vi.advanceTimersByTimeAsync(2000) })
    expect(requests).toHaveLength(4)

    await act(async () => {
      requests.forEach((request, index) => request.resolve(responseFor(paths[index], 1)))
      await Promise.all(requests.map(request => request.promise))
    })
    await act(async () => { await vi.advanceTimersByTimeAsync(1999) })
    expect(requests).toHaveLength(4)
    await act(async () => { await vi.advanceTimersByTimeAsync(1) })
    expect(requests).toHaveLength(8)
    expect(paths).toContain('/api/v1/settlement')
  })

  it('applies each completed poll in order and continues polling', async () => {
    vi.useFakeTimers()
    let callCount = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const cycle = Math.floor(callCount / 4) + 1
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
    expect(callCount).toBe(8)
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
    expect(requests).toHaveLength(4)
    unmount()

    await act(async () => {
      requests.forEach((request, index) => request.resolve(responseFor(paths[index], 1)))
      await Promise.all(requests.map(request => request.promise))
      await vi.advanceTimersByTimeAsync(10000)
    })
    expect(requests).toHaveLength(4)
  })

  it('shows settlement survival metrics and citizen survival details', async () => {
    const deadCitizen = { ...citizen, citizenId: '2', name: 'Bram Vale', health: 0, isAlive: false, currentAction: 'Dead', actionPhase: 'None', deathMinute: 360, deathCause: 'starvation', hunger: 10000, rest: 9200 }
    vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const path = String(input)
      if (path.endsWith('/health')) return new Response('Healthy')
      if (path.endsWith('/status')) return new Response(JSON.stringify({ state: 'Running', worldMinute: 360, pendingEventCount: 1, worldSeed: '42', population: 1, totalPopulation: 2, livingPopulation: 1, deadPopulation: 1 }))
      if (path.endsWith('/settlement')) return new Response(JSON.stringify({ ...settlement, livingPopulation: 1, deadPopulation: 1, totalPopulation: 2, foodStored: 7, woodStored: 8, stoneStored: 9 }))
      return new Response(JSON.stringify([citizen, deadCitizen]))
    })
    render(<App />)
    expect(await screen.findByRole('heading', { name: 'Elara Venn' })).toBeInTheDocument()
    expect(screen.getByText('Living population')).toBeInTheDocument()
    expect(screen.getByText('Deaths')).toBeInTheDocument()
    expect(screen.getByText('Food')).toBeInTheDocument()
    expect(screen.getByText('Wood')).toBeInTheDocument()
    expect(screen.getByText('Stone')).toBeInTheDocument()
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
})
