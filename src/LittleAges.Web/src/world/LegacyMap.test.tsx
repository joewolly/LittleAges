import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { Citizen, Map as WorldMap, SettlementSite } from '../api'
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
  it('selects all crowd members at their painted positions and prunes departed targets', () => {
    const markers: number[][] = []
    const context = {
      setTransform: vi.fn(), imageSmoothingEnabled: true, fillStyle: '', strokeStyle: '', lineWidth: 1, globalAlpha: 1,
      fillRect: vi.fn((...args: number[]) => { if (context.fillStyle === '#302a24') markers.push(args) }),
      strokeRect: vi.fn(), save: vi.fn(), restore: vi.fn(), beginPath: vi.fn(), rect: vi.fn(), clip: vi.fn(),
    }
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as unknown as CanvasRenderingContext2D)
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: 640, height: 480, top: 0, left: 0, right: 640, bottom: 480, x: 0, y: 0, toJSON: () => ({}) })
    const citizens = ['11', '13'].map(citizenId => ({ citizenId, isAlive: true, location: { x: 4, y: 4 }, currentAction: 'LivingWork' }) as Citizen)
    const pick = vi.fn(), select = vi.fn()
    const { container, rerender } = render(<LegacyMap map={map} citizens={citizens} structures={[]} selectedCitizenId="13" onPickCitizens={pick} onSelectCitizen={select} />)
    expect(markers[0].slice(0, 2)).not.toEqual(markers[1].slice(0, 2))
    expect(context.strokeRect).toHaveBeenCalledTimes(2) // starting tile and selected person
    const [x, y, width, height] = markers[1]
    fireEvent.click(container.querySelector('canvas')!, { clientX: x + width / 2, clientY: y + height / 2 })
    expect(pick).toHaveBeenCalledWith(['11', '13'])
    markers.length = 0
    rerender(<LegacyMap map={map} citizens={[citizens[0], { ...citizens[1], newcomer: { phase: 'Departed' } } as Citizen]} structures={[]} onPickCitizens={pick} onSelectCitizen={select} />)
    const [a, b, w, h] = markers[0]
    fireEvent.click(container.querySelector('canvas')!, { clientX: a + w / 2, clientY: b + h / 2 })
    expect(select).toHaveBeenCalledWith('11')
    expect(pick).toHaveBeenCalledOnce()
  })

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

  it('draws tracks, trails, and roads with distinct strokes and no overlay for worlds without roads', () => {
    const strokes: Array<{ color: string; width: number; dashed: boolean }> = []
    const context = {
      setTransform: vi.fn(), imageSmoothingEnabled: true, fillStyle: '', strokeStyle: '', lineWidth: 1, globalAlpha: 1, lineCap: 'butt', dash: [] as number[],
      fillRect: vi.fn(), strokeRect: vi.fn(), save: vi.fn(), restore: vi.fn(), beginPath: vi.fn(), rect: vi.fn(), clip: vi.fn(), arc: vi.fn(), fill: vi.fn(),
      moveTo: vi.fn(), lineTo: vi.fn(), setLineDash: vi.fn((dash: number[]) => { context.dash = dash }),
      stroke: vi.fn(() => strokes.push({ color: context.strokeStyle, width: context.lineWidth, dashed: context.dash.length > 0 })),
    }
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as unknown as CanvasRenderingContext2D)
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: 640, height: 480, top: 0, left: 0, right: 640, bottom: 480, x: 0, y: 0, toJSON: () => ({}) })

    const { rerender } = render(<LegacyMap map={map} structures={[]} citizens={[]} roads={null} />)
    expect(context.lineTo).not.toHaveBeenCalled()

    rerender(<LegacyMap map={map} structures={[]} citizens={[]} roads={{ tiles: [
      { x: 4, y: 4, grade: 'Track' }, { x: 5, y: 4, grade: 'Track' },
      { x: 4, y: 6, grade: 'Trail' }, { x: 5, y: 6, grade: 'Trail' },
      { x: 4, y: 8, grade: 'Road' }, { x: 5, y: 8, grade: 'Road' },
    ] }} />)
    expect(screen.getByRole('img', { name: /dashed tan tracks, brown trails, and wide gray roads/ })).toBeInTheDocument()
    const paths = strokes.filter(entry => entry.color !== '#f7f0e5')
    const track = paths.find(entry => entry.color === '#9c7b4c')!
    const trail = paths.find(entry => entry.color === '#6b4527')!
    const road = paths.find(entry => entry.color === '#67625c')!
    expect(track.dashed).toBe(true)
    expect(trail.dashed).toBe(false)
    expect(track.width).toBeLessThan(trail.width)
    expect(trail.width).toBeLessThan(road.width)
    expect(paths.some(entry => entry.color === '#efe6d2')).toBe(true)
  })
})
