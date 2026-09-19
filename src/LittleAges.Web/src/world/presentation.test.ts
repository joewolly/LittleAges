import { describe, expect, it } from 'vitest'
import type { Citizen, Structure } from '../api'
import { doorway, doorwayPlan, PresentationClock, restingHome, retainStructures } from './presentation'
import { positionAlongMovementPlan, scenePointAlongMovementPlan } from './visuals'

describe('continuous observer presentation', () => {
  it.each([1, 5, 10])('advances smoothly at %s min/s, freezes on pause and bounds delayed frames', speed => {
    const clock = new PresentationClock()
    clock.observe(100, speed, false, 1000)
    expect(clock.at(1100)).toBeCloseTo(100 + speed * 0.1)
    expect(clock.at(1150)).toBeGreaterThan(clock.at(1100))
    expect(clock.at(10000)).toBeLessThanOrEqual(100 + Math.max(0.25, 1 / speed + 0.1) * speed)
    clock.observe(102, speed, true, 1200)
    expect(clock.at(9000)).toBe(102)
    clock.observe(102, speed, false, 9000)
    expect(clock.at(9100)).toBeCloseTo(102 + speed * 0.1)
    clock.observe(20, speed, false, 9200)
    expect(clock.at(9200)).toBe(20)
  })

  it('keeps the same position and height when a mid-segment observation refreshes', () => {
    const plan = { actionSequence: 1, observedMinute: 100, segmentStartedMinute: 100, waypoints: [{ x: 0, y: 0, arriveMinute: 100 }, { x: 1, y: 0, arriveMinute: 110 }] }
    const refresh = { ...plan, observedMinute: 104, waypoints: [{ x: 0, y: 0, arriveMinute: 104 }, plan.waypoints[1]] }
    const map = { width: 2, height: 1, terrain: [1, 1], elevation: [0, 10000], resources: [], startingSite: { x: 0, y: 0 } }
    expect(positionAlongMovementPlan(refresh, 106)).toEqual({ x: 0.6, y: 0 })
    expect(scenePointAlongMovementPlan(map, refresh, 106)).toEqual(scenePointAlongMovementPlan(map, plan, 106))
    expect(positionAlongMovementPlan(refresh, 500)).toEqual({ x: 1, y: 0 })
  })
})

describe('house presentation', () => {
  const house = { structureId: '4', type: 'Shelter', status: 'Complete', location: { x: 10, y: 10 } } as Structure
  const citizen = { isAlive: true, homeStructureId: '4', currentAction: 'Rest', actionPhase: 'Perform', location: { x: 10, y: 10 }, movementPlan: null } as Citizen

  it('hides only living residents performing rest at their completed home', () => {
    expect(restingHome(citizen, [house])).toBe(house)
    for (const change of [{ isAlive: false }, { actionPhase: 'Travel' }, { currentAction: 'Build' }, { homeStructureId: null }, { location: { x: 9, y: 10 } }])
      expect(restingHome({ ...citizen, ...change } as Citizen, [house])).toBeNull()
    expect(restingHome(citizen, [{ ...house, status: 'UnderConstruction' }])).toBeNull()
  })

  it('uses the doorway on entry and exit without mutating authoritative coordinates or times', () => {
    const movementPlan = { actionSequence: 1, observedMinute: 0, waypoints: [{ x: 9, y: 10, arriveMinute: 0 }, { x: 10, y: 10, arriveMinute: 10 }] }
    const entry = doorwayPlan({ ...citizen, movementPlan }, [house])!
    expect(entry.waypoints[1]).toEqual({ ...doorway(house), arriveMinute: 10 })
    expect(movementPlan.waypoints[1]).toEqual({ x: 10, y: 10, arriveMinute: 10 })
    const exit = doorwayPlan({ ...citizen, currentAction: 'GatherFood', movementPlan: { ...movementPlan, waypoints: [...movementPlan.waypoints].reverse() } }, [house])!
    expect(exit.waypoints[0]).toEqual({ ...doorway(house), arriveMinute: 10 })
    expect(doorwayPlan({ ...citizen, currentAction: 'Build', movementPlan }, [house])).toBe(movementPlan)
  })

  it('retains unchanged scene objects and replaces changed ones', () => {
    const previous = [house]
    expect(retainStructures(previous, [{ ...house }])).toBe(previous)
    expect(retainStructures(previous, [{ ...house, condition: 12 }])[0]).not.toBe(house)
  })
})
