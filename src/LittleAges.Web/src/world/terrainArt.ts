import * as THREE from 'three'
import type { Map } from '../api'
import { groundPixels } from './terrainPixels'

/** A quick first paint; the worker replaces it with the full texture asynchronously. */
export function createGroundTexture(map: Map, seed: string | null): THREE.DataTexture {
  const texture = new THREE.DataTexture(groundPixels(map, seed, 1), map.width, map.height)
  texture.colorSpace = THREE.SRGBColorSpace
  texture.magFilter = THREE.LinearFilter
  texture.minFilter = THREE.LinearMipmapLinearFilter
  texture.generateMipmaps = true
  texture.needsUpdate = true
  return texture
}
