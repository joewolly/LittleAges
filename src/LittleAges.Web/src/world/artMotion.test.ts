import { describe, expect, it } from 'vitest'
import * as THREE from 'three'
import { citizenAnimation, setAnimationPlayback } from './artMotion'

describe('authoritative activity presentation', () => {
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
