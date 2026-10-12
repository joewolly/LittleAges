import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchBiography, fetchHealth, fetchRoads, fetchStatus } from './api'
import { fetchLivingWorld } from './living'
import { OBSERVER_READ_TIMEOUT_MS } from './observerRequest'

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('observer read deadlines', () => {
  it.each([
    ['status', fetchStatus],
    ['health', fetchHealth],
    ['biography', () => fetchBiography('9223372036854775807')],
    ['roads', fetchRoads],
    ['living', fetchLivingWorld],
  ])('bounds a stalled %s request and aborts the network read', async (_, read) => {
    vi.useFakeTimers()
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockReturnValue(new Promise<Response>(() => {}))
    const result = read()
    const rejected = expect(result).rejects.toThrow('The server did not respond within 15 seconds.')
    const signal = fetchMock.mock.calls[0][1]!.signal!
    await vi.advanceTimersByTimeAsync(OBSERVER_READ_TIMEOUT_MS - 1)
    expect(signal.aborted).toBe(false)
    await vi.advanceTimersByTimeAsync(1)
    await rejected
    expect(signal.aborted).toBe(true)
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(vi.getTimerCount()).toBe(0)
  })

  it.each([['health', fetchHealth], ['living', fetchLivingWorld]])('bounds a stalled %s response body after headers arrive', async (_, read) => {
    vi.useFakeTimers()
    const response = new Response('Healthy')
    vi.spyOn(response, 'text').mockReturnValue(new Promise<string>(() => {}))
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(response)
    const rejected = expect(read()).rejects.toThrow('did not respond within 15 seconds')
    await vi.advanceTimersByTimeAsync(OBSERVER_READ_TIMEOUT_MS)
    await rejected
    expect(fetchMock.mock.calls[0][1]!.signal!.aborted).toBe(true)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('clears the deadline after a successful plain-text read without aborting it later', async () => {
    vi.useFakeTimers()
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('Healthy'))
    await expect(fetchHealth()).resolves.toEqual({ ok: true, label: 'Healthy' })
    expect(vi.getTimerCount()).toBe(0)
    await vi.advanceTimersByTimeAsync(OBSERVER_READ_TIMEOUT_MS)
    expect(fetchMock.mock.calls[0][1]!.signal!.aborted).toBe(false)
  })

  it('includes slow headers and body consumption in the same deadline', async () => {
    vi.useFakeTimers()
    const response = new Response('Healthy')
    const text = vi.spyOn(response, 'text').mockReturnValue(new Promise<string>(() => {}))
    vi.spyOn(globalThis, 'fetch').mockImplementation(() => new Promise(resolve => {
      setTimeout(() => resolve(response), OBSERVER_READ_TIMEOUT_MS - 1000)
    }))
    const rejected = expect(fetchHealth()).rejects.toThrow('did not respond within 15 seconds')
    await vi.advanceTimersByTimeAsync(OBSERVER_READ_TIMEOUT_MS - 1000)
    expect(text).toHaveBeenCalledOnce()
    await vi.advanceTimersByTimeAsync(1000)
    await rejected
    expect(vi.getTimerCount()).toBe(0)
  })

  it('preserves network failures and clears their deadlines', async () => {
    vi.useFakeTimers()
    const failure = new TypeError('Failed to fetch')
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(failure)
    await expect(fetchStatus()).rejects.toBe(failure)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('preserves HTTP failures and clears their deadlines', async () => {
    vi.useFakeTimers()
    vi.spyOn(globalThis, 'fetch').mockImplementation(async () => new Response('Unavailable', { status: 503 }))
    await expect(fetchStatus()).rejects.toThrow('Request failed (503)')
    await expect(fetchLivingWorld()).rejects.toThrow('Living settlement could not be read.')
    expect(vi.getTimerCount()).toBe(0)
  })

  it('retains optional endpoint compatibility without leaving deadlines behind', async () => {
    vi.useFakeTimers()
    vi.spyOn(globalThis, 'fetch').mockImplementation(async () => new Response(null, { status: 404 }))
    await expect(fetchRoads()).resolves.toBeNull()
    await expect(fetchLivingWorld()).resolves.toBeNull()
    expect(vi.getTimerCount()).toBe(0)
  })
})
