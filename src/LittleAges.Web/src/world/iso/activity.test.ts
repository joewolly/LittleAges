import { describe, expect, it } from 'vitest'
import type { Citizen } from '../../api'
import type { LivingOrder } from '../../living'
import { activityPose, citizenMotion } from './activity'

const citizen = { citizenId: '11', isAlive: true, currentAction: 'LivingWork', actionPhase: 'Perform', movementPlan: null, location: { x: 105, y: 60 } } as Citizen
const order = { citizenId: '11', kind: 'Experiment', phase: 'Work', workDone: 360, requiredWork: 480, blockedReason: '' } as LivingOrder

describe('observed villager activity motion', () => {
  it('distinguishes leisure from productive work using the assigned order', () => {
    expect(citizenMotion(citizen, order)).toBe('experiment')
    expect(citizenMotion(citizen, { ...order, kind: 'Recreate' })).toBe('leisure')
    expect(citizenMotion(citizen, { ...order, kind: 'AttendFestival' })).toBe('celebrate')
    expect(citizenMotion({ ...citizen, currentAction: 'GatherWood' }, undefined)).toBe('chop')
    expect(citizenMotion({ ...citizen, currentAction: 'Socialize' }, undefined)).toBe('social')
    expect(citizenMotion({ ...citizen, currentAction: 'Idle' }, undefined)).toBe('idle')
  })

  it('never suggests work for missing, mismatched, blocked, completed or queued claims', () => {
    expect(citizenMotion(citizen, undefined)).toBe('still')
    for (const change of [{ citizenId: '12' }, { phase: 'Collect' }, { phase: 'Travel' }, { phase: 'Deliver' }, { blockedReason: 'MissingInputs' }, { workDone: 480 }]) {
      expect(citizenMotion(citizen, { ...order, ...change })).toBe('still')
    }
    expect(citizenMotion({ ...citizen, isAlive: false }, order)).toBe('still')
    expect(citizenMotion({ ...citizen, actionPhase: 'WaitingForStorage' }, order)).toBe('still')
    expect(citizenMotion({ ...citizen, actionPhase: 'None' }, order)).toBe('still')
    const movementPlan = { actionSequence: 1, observedMinute: 100, waypoints: [{ x: 105, y: 60, arriveMinute: 100 }, { x: 106, y: 60, arriveMinute: 110 }] }
    expect(citizenMotion({ ...citizen, movementPlan }, order)).toBe('still')
  })

  it('freezes all cosmetic motion when animation is disabled', () => {
    for (const kind of ['idle', 'leisure', 'experiment', 'chop', 'celebrate', 'eat'] as const) {
      expect(activityPose(kind, '11', 1000, false, false)).toEqual(activityPose(kind, '11', 9000, true, false))
      expect(activityPose(kind, '11', 1000, false, false)).toEqual({ frame: 0, lift: 0, lean: 0 })
    }
  })

  it('animates actual work without walking or changing citizen/order state', () => {
    const before = JSON.stringify({ citizen, order })
    const kind = citizenMotion(citizen, order)
    const first = activityPose(kind, citizen.citizenId, 1000, false, true)
    const second = activityPose(kind, citizen.citizenId, 1400, false, true)
    expect(first.lean).not.toBe(second.lean)
    expect(first.gesture?.kind).toBe('experiment')
    expect(first.frame).toBe(0)
    expect(first.lift).toBe(0)
    expect(JSON.stringify({ citizen, order })).toBe(before)
    expect(activityPose('idle', citizen.citizenId, 1000, false, true).gesture).toBeUndefined()
    expect(activityPose('still', citizen.citizenId, 1000, false, true)).toEqual({ frame: 0, lift: 0, lean: 0 })
  })
})
