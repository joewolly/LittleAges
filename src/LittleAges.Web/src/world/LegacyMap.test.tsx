import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { Map as WorldMap, SettlementSite } from '../api'
import { LegacyMap } from './LegacyMap'

const map: WorldMap = { width: 32, height: 32, terrain: Array.from({ length: 32 * 32 }, () => 1), elevation: Array.from({ length: 32 * 32 }, () => 0), resources: [], startingSite: { x: 2, y: 2 } }
const sites: SettlementSite[] = [
  { settlementId: '1', site: { x: 2, y: 2 }, livingPopulation: 4, foodStored: 20, woodStored: 0, stoneStored: 0 },
  { settlementId: '2', site: { x: 25, y: 26 }, livingPopulation: 7, foodStored: 50, woodStored: 4, stoneStored: 3 },
]

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('legacy settlement map sites', () => {
  it('draws both site markers and crops around the focused site', () => {
    const markerColors: string[] = []
    const context = {
      setTransform: vi.fn(), imageSmoothingEnabled: true, fillStyle: '', strokeStyle: '', lineWidth: 1, globalAlpha: 1,
      fillRect: vi.fn(), strokeRect: vi.fn(), save: vi.fn(), restore: vi.fn(), beginPath: vi.fn(), rect: vi.fn(), clip: vi.fn(), arc: vi.fn(), stroke: vi.fn(),
      fill: vi.fn(() => markerColors.push(context.fillStyle)),
    }
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as unknown as CanvasRenderingContext2D)
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: 640, height: 480, top: 0, left: 0, right: 640, bottom: 480, x: 0, y: 0, toJSON: () => ({}) })

    render(<LegacyMap map={map} structures={[]} citizens={[]} settlementSites={sites} selectedSettlementId="2" focusedSettlementId="2" />)

    expect(screen.getByRole('img', { name: /focused on settlement 2 at \(25, 26\)/ })).toBeInTheDocument()
    expect(context.arc).toHaveBeenCalledTimes(2)
    expect(markerColors).toEqual(['#523f31', '#f3d287'])
    expect(context.fillRect).toHaveBeenCalledTimes(211)
  })
})
