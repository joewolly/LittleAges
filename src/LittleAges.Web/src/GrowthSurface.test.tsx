import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { GrowthSurface } from './GrowthSurface'
import { parseGrowth } from './growthApi'

const observation = { living: 24, births: 5, deaths: 1, unpartneredAdults: 3, unhoused: 0, foodReserveTarget: 1440,
  deathCauses: { natural: 1 }, households: [{ householdId: '9223372036854775806', ready: false, blockers: ['Insufficient food reserve'] }] }
afterEach(() => { cleanup(); vi.unstubAllGlobals() })
it('explains blocked growth with lossless household IDs when opened', async () => {
  const request = vi.fn(async () => new Response(JSON.stringify(observation)))
  vi.stubGlobal('fetch', request)
  render(<GrowthSurface />)
  expect(request).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Population outlook' }))
  expect(await screen.findByText('Insufficient food reserve')).toBeTruthy()
  expect(screen.getByText('Household 9223372036854775806')).toBeTruthy()
  expect(screen.getByText('24')).toBeTruthy()
})
it('discards responses after closing and aborts the outstanding request', async () => {
  let resolve!: (value: Response) => void
  const request = vi.fn((_path, options) => new Promise<Response>(done => { resolve = done; expect(options.signal.aborted).toBe(false) }))
  vi.stubGlobal('fetch', request)
  render(<GrowthSurface />)
  const button = screen.getByRole('button', { name: 'Population outlook' })
  fireEvent.click(button)
  fireEvent.click(button)
  expect(request.mock.calls[0][1].signal.aborted).toBe(true)
  await act(async () => { resolve(new Response(JSON.stringify(observation))) })
  expect(screen.queryByText('Insufficient food reserve')).toBeNull()
})
it('rejects malformed counts and household identities', () => {
  expect(() => parseGrowth({ ...observation, living: -1 })).toThrow()
  expect(() => parseGrowth({ ...observation, households: [{ ...observation.households[0], householdId: 3 }] })).toThrow()
  expect(() => parseGrowth({ ...observation, households: [...observation.households, ...observation.households] })).toThrow()
})
