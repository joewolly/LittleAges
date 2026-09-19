import * as THREE from 'three'
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js'
import type { ResourceType } from '../api'

/** Distant silhouettes retain the authored canopy clusters and visible trunks. */
export function createResourceLod(type: ResourceType): THREE.BufferGeometry {
  const parts: THREE.BufferGeometry[] = []
  const add = (geometry: THREE.BufferGeometry, x: number, y: number, z: number, color: string, scale: [number, number, number] = [1, 1, 1]) => {
    const part = geometry.index ? geometry.toNonIndexed() : geometry
    part.scale(...scale).translate(x, y, z)
    const rgb = new THREE.Color(color), colors = new Float32Array(part.getAttribute('position').count * 3)
    for (let i = 0; i < colors.length; i += 3) { colors[i] = rgb.r; colors[i + 1] = rgb.g; colors[i + 2] = rgb.b }
    part.setAttribute('color', new THREE.BufferAttribute(colors, 3))
    part.deleteAttribute('uv')
    parts.push(part)
    if (geometry !== part) geometry.dispose()
  }
  if (type === 'Wood') {
    add(new THREE.CylinderGeometry(0.15, 0.21, 1.9, 6), 0, 0.95, 0, '#75502b')
    for (const [x, z, y, size] of [[-0.45, 0, 1.55, 0.65], [0.43, 0.1, 1.65, 0.67], [0, -0.38, 1.87, 0.67], [0, 0.35, 2.08, 0.64], [-0.12, 0, 2.4, 0.59]]) {
      add(new THREE.SphereGeometry(size, 8, 5), x, y, z, y > 2 ? '#99c64e' : '#79b63a', [1, 0.8, 0.88])
    }
  } else if (type === 'Food') {
    for (const [x, z] of [[-0.22, 0.05], [0.25, 0.1], [0, -0.18]]) {
      add(new THREE.SphereGeometry(0.31, 7, 4), x, 0.3, z, '#6aa239')
      add(new THREE.SphereGeometry(0.075, 5, 3), x, 0.55, z - 0.1, '#c6394c')
    }
  } else {
    for (const [x, z, size] of [[-0.25, 0.02, 0.35], [0.19, 0.12, 0.45], [0.05, -0.27, 0.26]]) add(new THREE.IcosahedronGeometry(size, 0), x, size * 0.5, z, '#a5b3b1', [1, 0.75, 1])
  }
  const merged = mergeGeometries(parts)
  parts.forEach(part => part.dispose())
  return merged
}
