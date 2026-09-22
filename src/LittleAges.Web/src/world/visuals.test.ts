import { describe, expect, it } from 'vitest'
import type { Citizen, Map as WorldMap } from '../api'
import { citizenPaletteIndex, containMap, detailVariant, elevationAt, movementPlanIdentity, positionAlongMovementPlan, presentationSegmentProgress, reconcileVisualMinute, scenePointAlongMovementPlan, shouldInterpolateCitizen, stableVisualHash, worldToScene } from './visuals'

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

  it('fits the fallback map without stretching it to the viewport aspect ratio', () => {
    const layout = containMap(1440, 658, 160, 160)
    expect(layout.drawWidth).toBe(layout.drawHeight)
    expect(layout.offsetX).toBeGreaterThan(layout.offsetY)
    expect(layout.drawHeight).toBeLessThanOrEqual(658)
  })

  it('interpolates along authoritative route segments and clamps at both ends', () => {
    const plan = { actionSequence: 3, observedMinute: 10, waypoints: [{ x: 0, y: 0, arriveMinute: 10 }, { x: 1, y: 0, arriveMinute: 20 }, { x: 1, y: 1, arriveMinute: 40 }] }
    expect(positionAlongMovementPlan(plan, 5)).toEqual({ x: 0, y: 0 })
    expect(positionAlongMovementPlan(plan, 15)).toEqual({ x: 0.5, y: 0 })
    expect(positionAlongMovementPlan(plan, 30)).toEqual({ x: 1, y: 0.5 })
    expect(positionAlongMovementPlan(plan, 50)).toEqual({ x: 1, y: 1 })
    expect(scenePointAlongMovementPlan(map, plan, 15, 0.1)).toMatchObject({ x: 0, z: -0.5 })
  })

  it('interpolates only short same-action moves at readable speeds', () => {
    expect(shouldInterpolateCitizen(citizen, { ...citizen, location: { x: 1, y: 1 } }, 10, false)).toBe(true)
    expect(shouldInterpolateCitizen(citizen, { ...citizen, location: { x: 3, y: 0 } }, 10, false)).toBe(false)
    expect(shouldInterpolateCitizen(citizen, { ...citizen, location: { x: 1, y: 0 }, actionSequence: 4 }, 10, false)).toBe(false)
    expect(shouldInterpolateCitizen(citizen, { ...citizen, location: { x: 1, y: 0 } }, 50, false)).toBe(false)
    expect(shouldInterpolateCitizen(citizen, { ...citizen, location: { x: 1, y: 0 } }, 10, true)).toBe(false)
  })

  it('shows brisk steps at Normal pace without changing arrival or faster presets', () => {
    expect(presentationSegmentProgress(100, 114, 108, 1.44)).toBe(0)
    expect(presentationSegmentProgress(100, 114, 112.2, 1.44)).toBeCloseTo(.5)
    expect(presentationSegmentProgress(100, 114, 114, 1.44)).toBe(1)
    expect(presentationSegmentProgress(100, 114, 107, 4.32)).toBe(.5)
    expect(presentationSegmentProgress(100, 114, 107, null)).toBe(.5)
  })

  it('keeps the visual clock continuous when authority refreshes the same route', () => {
    const first = { actionSequence: 7, observedMinute: 100, waypoints: [{ x: 1, y: 1, arriveMinute: 100 }, { x: 2, y: 1, arriveMinute: 110 }, { x: 3, y: 1, arriveMinute: 120 }] }
    const refreshed = { actionSequence: 7, observedMinute: 104, waypoints: [{ x: 1, y: 1, arriveMinute: 104 }, { x: 2, y: 1, arriveMinute: 110 }, { x: 3, y: 1, arriveMinute: 120 }] }
    const changed = { actionSequence: 8, observedMinute: 106, waypoints: [{ x: 1, y: 1, arriveMinute: 106 }, { x: 1, y: 2, arriveMinute: 116 }] }
    expect(movementPlanIdentity(first)).toBe(movementPlanIdentity(refreshed))
    expect(reconcileVisualMinute(107.5, movementPlanIdentity(first), refreshed)).toBe(107.5)
    expect(reconcileVisualMinute(107.5, movementPlanIdentity(first), changed)).toBe(106)
  })
})
