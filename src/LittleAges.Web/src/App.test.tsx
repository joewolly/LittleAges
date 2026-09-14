import { act, cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

const citizen = { citizenId: '9223372036854775807', name: 'Elara Venn', age: 18, lifeStage: 'Adult', location: { x: 1, y: 2 }, health: 10000, currentAction: 'Idle', actionSequence: 0 }

type Deferred<T> = { promise: Promise<T>; resolve: (value: T) => void }

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(nextResolve => { resolve = nextResolve })
  return { promise, resolve }
}

function responseFor(path: string, worldMinute: number, name = citizen.name) {
  if (path.endsWith('/health')) return new Response('Healthy')
  if (path.endsWith('/status')) return new Response(JSON.stringify({ state: 'Running', worldMinute, pendingEventCount: 1, worldSeed: '42' }))
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
      if (path.endsWith('/status')) return new Response(JSON.stringify({ state: 'Running', worldMinute: 10, pendingEventCount: 1, worldSeed: '42' }))
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
    expect(requests).toHaveLength(3)

    await act(async () => { await vi.advanceTimersByTimeAsync(2000) })
    expect(requests).toHaveLength(3)

    await act(async () => {
      requests.forEach((request, index) => request.resolve(responseFor(paths[index], 1)))
      await Promise.all(requests.map(request => request.promise))
    })
    await act(async () => { await vi.advanceTimersByTimeAsync(1999) })
    expect(requests).toHaveLength(3)
    await act(async () => { await vi.advanceTimersByTimeAsync(1) })
    expect(requests).toHaveLength(6)
  })

  it('applies each completed poll in order and continues polling', async () => {
    vi.useFakeTimers()
    let callCount = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
      const cycle = Math.floor(callCount / 3) + 1
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
    expect(callCount).toBe(6)
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
    expect(requests).toHaveLength(3)
    unmount()

    await act(async () => {
      requests.forEach((request, index) => request.resolve(responseFor(paths[index], 1)))
      await Promise.all(requests.map(request => request.promise))
      await vi.advanceTimersByTimeAsync(10000)
    })
    expect(requests).toHaveLength(3)
  })
})
