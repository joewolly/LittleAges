import * as THREE from 'three'
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js'

/** Open storage yards keep dense historical settlements readable from above. */
export function createStockpileLod(): THREE.BufferGeometry {
  const parts: THREE.BufferGeometry[] = []
  const addBox = (width: number, height: number, depth: number, x: number, y: number, z: number, color: string, rotation = 0) => {
    const geometry = new THREE.BoxGeometry(width, height, depth)
    geometry.applyMatrix4(new THREE.Matrix4().makeRotationZ(rotation))
    geometry.translate(x, y, z)
    const rgb = new THREE.Color(color)
    const colors = new Float32Array(geometry.getAttribute('position').count * 3)
    for (let index = 0; index < colors.length; index += 3) {
      colors[index] = rgb.r; colors[index + 1] = rgb.g; colors[index + 2] = rgb.b
    }
    geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3))
    parts.push(geometry)
  }
  addBox(1.7, .08, 1.6, 0, .045, 0, '#806345')
  addBox(.43, .27, .42, -.4, .21, -.31, '#c99f60')
  addBox(.38, .22, .38, .26, .185, -.25, '#aa7b48')
  addBox(.4, .19, .43, -.32, .17, .32, '#d6b574')
  addBox(.55, .13, .14, .32, .16, .34, '#805e3b')
  addBox(.55, .13, .14, .32, .3, .34, '#926e45')
  const merged = mergeGeometries(parts, false)
  parts.forEach(part => part.dispose())
  if (!merged) throw new Error('Could not create the stockpile low-detail model.')
  return merged
}
