import { describe, expect, it } from 'vitest'
import * as THREE from 'three'
import { worldGeometry } from './assetGeometry'

describe('instanced GLB geometry', () => {
  it('decodes normalized positions before applying the exported node transform', () => {
    const original = new THREE.BufferGeometry()
    original.setAttribute('position', new THREE.Int16BufferAttribute([0, -32767, 0, 0, 32767, 0], 3, true))
    const source = new THREE.Mesh(original)
    source.position.y = 1.5
    source.scale.setScalar(1.5)
    source.updateMatrixWorld()
    const transformed = worldGeometry(source)
    expect(transformed.getAttribute('position').getY(0)).toBeCloseTo(0)
    expect(transformed.getAttribute('position').getY(1)).toBeCloseTo(3)
    expect(original.getAttribute('position').getY(1)).toBe(1)
    transformed.dispose(); original.dispose()
  })
})
