import { describe, expect, it } from 'vitest'
import type { Citizen, Structure } from '../api'
import type { LivingOrder, LivingWorld } from '../living'
import { advanceCitizenPose, citizenActivity, doorway, doorwayPlan, PresentationClock, restingHome, retainStructures } from './presentation'
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

describe('factual activity and authority recovery', () => {
  const citizen = { citizenId: '11', isAlive: true, location: { x: 105, y: 60 }, currentAction: 'LivingWork', actionPhase: 'Perform', actionSequence: 1, movementPlan: null } as Citizen
  const order = { id: '519', citizenId: '11', kind: 'Experiment', phase: 'Work', workDone: 360, requiredWork: 480, blockedReason: '', ingredients: [], cargo: [] } as unknown as LivingOrder
  const world = (change: Partial<LivingOrder> = {}) => ({ orders: [{ ...order, ...change }] }) as LivingWorld
  const observation = { worldMinute: 100, visualMinute: 100, speed: 1, paused: false, reducedMotion: false }

  it('shows work progress, blockage, completion and queued phases without inventing productive motion', () => {
    expect(citizenActivity(citizen, world(), [])).toEqual({ label: 'Experiment · Work phase · 360 / 480', cue: 'W' })
    expect(citizenActivity(citizen, world({ blockedReason: 'MissingInputs' }), []).label).toContain('Blocked: Missing Inputs')
    expect(citizenActivity(citizen, world({ workDone: 480 }), []).label).toContain('Work complete')
    expect(citizenActivity({ ...citizen, actionPhase: 'None' }, world(), []).label).toContain('Waiting to work')
    expect(citizenActivity(citizen, world({ phase: 'Collect' }), []).cue).toBe('…')
    const pose = advanceCitizenPose(undefined, citizen, [], observation, .016)
    expect(advanceCitizenPose(pose, citizen, [], { ...observation, visualMinute: 110 }, .1).walking).toBe(false)
    expect(citizenActivity({ ...citizen, currentAction: 'Rest' }, world(), []).label).toBe('Resting')
    expect(citizenActivity({ ...citizen, currentAction: 'Eat' }, world(), []).label).toBe('Eating')
    expect(citizenActivity({ ...citizen, currentAction: 'Socialize' }, world(), []).cue).toBe('•')
    expect(citizenActivity(citizen, world({ kind: 'AttendFestival' }), []).cue).toBe('•')
    expect(citizenActivity({ ...citizen, currentAction: 'GatherWood', actionPhase: 'WaitingForStorage' }, world(), []).cue).toBe('!')
  })

  const traveler = { ...citizen, currentAction: 'GatherWood', actionPhase: 'TravelToTarget', movementPlan: { actionSequence: 1, observedMinute: 100, segmentStartedMinute: 100, waypoints: [{ x: 105, y: 60, arriveMinute: 100 }, { x: 106, y: 60, arriveMinute: 110 }] } } as Citizen
  it.each([
    { speed: 0 }, { speed: -1 }, { speed: null }, { speed: 10.01 }, { speed: 100 }, { paused: true }, { reducedMotion: true },
  ])('snaps canonical state with static travel cues at boundary %j', change => {
    const previous = { x: 105.8, y: 60, visible: true, walking: true, facingRight: true, actionSequence: 1, observedMinute: 100 }
    const pose = advanceCitizenPose(previous, traveler, [], { ...observation, visualMinute: 109, ...change }, .1)
    expect(pose.x).toBe(105)
    expect(pose.walking).toBe(false)
    expect(citizenActivity(traveler, null, []).label).toContain('Traveling')
  })

  it('keeps continuous timed travel, and discards stale smoothing after action or checkpoint recovery', () => {
    const first = advanceCitizenPose(undefined, traveler, [], observation, .1)
    expect(advanceCitizenPose(first, traveler, [], { ...observation, visualMinute: 109, speed: 10 }, .1).walking).toBe(true)
    expect(advanceCitizenPose(first, traveler, [], { ...observation, visualMinute: 105 }, .1).x).toBeGreaterThan(105)
    const moved = { ...first, x: 105.9, walking: true }
    expect(advanceCitizenPose(moved, { ...citizen, actionSequence: 2 }, [], observation, .1)).toMatchObject({ x: 105, walking: false })
    expect(advanceCitizenPose(moved, traveler, [], { ...observation, worldMinute: 20, visualMinute: 20 }, .1)).toMatchObject({ x: 105, walking: false })
  })

  it('keeps a slow walker animated even when smoothing has almost caught up', () => {
    const previous = { x: 105.499, y: 60, visible: true, walking: true, facingRight: true, actionSequence: 1, observedMinute: 100 }
    const pose = advanceCitizenPose(previous, traveler, [], { ...observation, visualMinute: 105, speed: 1.44 }, .016)
    expect(pose.walking).toBe(true)
    expect(pose.x).toBeLessThanOrEqual(105.5)
    expect(advanceCitizenPose(pose, traveler, [], { ...observation, visualMinute: 110, speed: 1.44 }, .016).walking).toBe(false)
  })

  it('keeps indoor rest and doorway exits, and keeps guests visible with factual visitor states', () => {
    const house = { structureId: '4', type: 'Shelter', status: 'Complete', location: citizen.location } as Structure
    const resting = { ...citizen, currentAction: 'Rest', homeStructureId: '4' } as Citizen
    const pose = advanceCitizenPose(undefined, resting, [house], { ...observation, paused: true }, .1)
    expect(pose).toMatchObject({ ...doorway(house), visible: false, walking: false })
    expect(citizenActivity(resting, world(), [house]).label).toBe('Resting indoors')
    const leaving = advanceCitizenPose(pose, { ...traveler, homeStructureId: '4' }, [house], observation, 0)
    expect(leaving).toMatchObject({ ...doorway(house), visible: true })
    const guest = { ...resting, newcomer: { phase: 'Visiting', joinedMinute: null } } as Citizen
    expect(advanceCitizenPose(undefined, guest, [house], observation, .1).visible).toBe(true)
    expect(citizenActivity(guest, world(), [house]).cue).toBe('V')
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
