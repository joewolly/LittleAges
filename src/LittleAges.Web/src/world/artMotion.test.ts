import { describe, expect, it } from 'vitest'
import * as THREE from 'three'
import { citizenAnimation, setAnimationPlayback } from './artMotion'
import { livingLabel, livingWorkStage, type LivingOrder } from '../living'

describe('authoritative activity presentation', () => {
  it('depicts living work and cargo from the authoritative order', () => {
    const work: LivingOrder = { id: '1', kind: 'Harvest', location: { x: 1, y: 1 }, citizenId: '1', subjectId: '2', technique: null, phase: 'Work', workDone: 0, requiredWork: 120, blockedReason: '', ingredients: [], cargo: [] }
    const citizen = { currentAction: 'LivingWork', actionPhase: 'Perform', carriedResource: null } as const
    expect(citizenAnimation(citizen, work)).toBe('Gather')
    expect(citizenAnimation(citizen, { ...work, kind: 'Teach' })).toBe('Socialize')
    expect(citizenAnimation({ ...citizen, actionPhase: 'TravelToTarget' }, { ...work, phase: 'Deliver', cargoInTransit: true, cargo: [{ good: 'Grain', quantity: 60 }] })).toBe('Carry')
    expect(citizenAnimation({ ...citizen, actionPhase: 'TravelToTarget' }, work)).toBe('Walk')
    expect(work.cargo).toEqual([])
  })
  it('presents M15 road building as construction carrying stone', () => {
    const road: LivingOrder = { id: '1', kind: 'BuildRoad', location: { x: 1, y: 1 }, citizenId: '1', subjectId: null, technique: null, phase: 'Work', workDone: 60, requiredWork: 120, blockedReason: '', ingredients: [{ resource: 'Stone', quantity: 2 }], cargo: [] }
    const citizen = { currentAction: 'LivingWork', actionPhase: 'Perform', carriedResource: null } as const
    expect(citizenAnimation(citizen, road)).toBe('Build')
    expect(citizenAnimation({ ...citizen, actionPhase: 'TravelToTarget' }, { ...road, phase: 'Travel' })).toBe('Carry')
    expect(livingLabel(road.kind)).toBe('Build Road')
    expect(livingWorkStage({ ...road, phase: 'Travel', suppliesDelivered: true })).toBe('Going to the work site')
  })
  it('walks to a build or rest target instead of playing a work or rest pose in transit', () => {
    for (const currentAction of ['Build', 'Rest', 'Socialize'] as const) {
      expect(citizenAnimation({ currentAction, actionPhase: 'TravelToTarget', carriedResource: null })).toBe('Walk')
    }
    expect(citizenAnimation({ currentAction: 'HaulConstruction', actionPhase: 'TransportToConstruction', carriedResource: 'Wood' })).toBe('Carry')
    expect(citizenAnimation({ currentAction: 'Build', actionPhase: 'Perform', carriedResource: null })).toBe('Build')
  })
  it('freezes both clip position and mixer time until resumed', () => {
    const object = new THREE.Object3D(), mixer = new THREE.AnimationMixer(object)
    const action = mixer.clipAction(new THREE.AnimationClip('Walk', 1, [new THREE.NumberKeyframeTrack('.position[x]', [0, 1], [0, 1])])).play()
    mixer.update(0.2)
    const position = object.position.x, time = mixer.time
    setAnimationPlayback(mixer, true, false, 1)
    mixer.update(0.3)
    expect(object.position.x).toBe(position); expect(mixer.time).toBe(time)
    setAnimationPlayback(mixer, false, false, 1)
    mixer.update(0.2)
    expect(object.position.x).toBeGreaterThan(position)
    setAnimationPlayback(mixer, false, true, 1); expect(mixer.timeScale).toBe(0)
    setAnimationPlayback(mixer, false, false, 50); expect(mixer.timeScale).toBe(0)
    action.stop(); mixer.uncacheRoot(object)
  })
})
