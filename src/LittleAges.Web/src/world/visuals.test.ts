import { describe, expect, it } from 'vitest'
import type { Citizen, Map as WorldMap } from '../api'
import { citizenPaletteIndex, detailVariant, elevationAt, shouldInterpolateCitizen, stableVisualHash, worldToScene } from './visuals'

const map: WorldMap = { width: 2, height: 2, terrain: [1, 2, 3, 4], elevation: [0, 5000, 10000, 2500], resources: [], startingSite: { x: 0, y: 0 } }
const citizen = { citizenId: '7', location: { x: 0, y: 0 }, actionSequence: 3 } as Citizen

describe('diorama visual projection', () => {
  it('uses stable seed-scoped visual hashes', () => {
    expect(stableVisualHash('42', 'tree', 5)).toBe(stableVisualHash('42', 'tree', 5))
    expect(detailVariant('42', 'tree', 5)).not.toBe(detailVariant('43', 'tree', 5))
    expect(citizenPaletteIndex('42', '7')).toBeGreaterThanOrEqual(0)
    expect(citizenPaletteIndex('42', '7')).toBeLessThan(8)
  })

  it('maps canonical coordinates into centered scene coordinates with compressed relief', () => {
    expect(elevationAt(map, 1, 0)).toBe(1.5)
    expect(worldToScene(map, 1, 1, 0.2)).toEqual({ x: 0.5, y: 0.95, z: 0.5 })
  })

  it('interpolates only short same-action moves at readable speeds', () => {
    expect(shouldInterpolateCitizen(citizen, { ...citizen, location: { x: 1, y: 1 } }, 10, false)).toBe(true)
    expect(shouldInterpolateCitizen(citizen, { ...citizen, location: { x: 3, y: 0 } }, 10, false)).toBe(false)
    expect(shouldInterpolateCitizen(citizen, { ...citizen, location: { x: 1, y: 0 }, actionSequence: 4 }, 10, false)).toBe(false)
    expect(shouldInterpolateCitizen(citizen, { ...citizen, location: { x: 1, y: 0 } }, 50, false)).toBe(false)
    expect(shouldInterpolateCitizen(citizen, { ...citizen, location: { x: 1, y: 0 } }, 10, true)).toBe(false)
  })
})
