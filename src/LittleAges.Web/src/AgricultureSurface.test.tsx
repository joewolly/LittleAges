import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { AgricultureSurface } from './AgricultureSurface'
import { parseAgriculture } from './agricultureApi'

const data = { enabled: true, version: 1, food: 19000, dedicatedFoodCapacity: 20000, winterReserveTarget: 16200, projectedCoverageDays: 105,
  farms: [{ structureId: '9223372036854775806', year: 0, stage: 'Harvest', plantingWork: 1200, tendingWork: 2400, yield: 7000, remaining: 2000, harvested: 5000 }],
  harvests: [{ structureId: '9223372036854775806', year: 0, yield: 7000, harvested: 5000, lost: 2000 }], totalFarms: 1, totalHarvests: 51, offset: 0, limit: 50 }
afterEach(() => { cleanup(); vi.unstubAllGlobals() })
it('shows actual crop work, reserves, harvest loss, and bounded record navigation', async () => {
  const request = vi.fn(async (url: string) => new Response(JSON.stringify(url.startsWith('/api/v1/agriculture?') ? data : [])))
  vi.stubGlobal('fetch', request)
  render(<AgricultureSurface worldMinute={388800} />)
  expect(request).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Farming and reserves' }))
  expect(await screen.findByText('Farm 9223372036854775806 · Harvest')).toBeTruthy()
  expect(screen.getByText(/2000 left unharvested/)).toBeTruthy()
  expect(screen.getByRole('img', { name: 'Monthly food production and consumption' })).toBeTruthy()
  expect(request.mock.calls.some(([url]) => url.startsWith('/api/v1/statistics?'))).toBe(true)
  fireEvent.click(screen.getByRole('button', { name: 'Next records' }))
  expect(request.mock.calls.some(([url]) => url.includes('offset=50'))).toBe(true)
})
it('rejects lossless-ID violations, impossible harvest accounting, and unknown versions', () => {
  expect(() => parseAgriculture({ ...data, version: 2 })).toThrow()
  expect(() => parseAgriculture({ ...data, farms: [{ ...data.farms[0], structureId: '9223372036854775808' }] })).toThrow()
  expect(() => parseAgriculture({ ...data, harvests: [{ ...data.harvests[0], lost: 0 }] })).toThrow()
})
