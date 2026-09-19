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

export function upgradeGroundTexture(texture: THREE.DataTexture, map: Pick<Map, 'width' | 'height'>, pixels: Uint8Array<ArrayBuffer>): void {
  // WebGL allocates immutable texture storage on first upload. Release that
  // allocation so Three creates new storage for the larger worker image.
  texture.dispose()
  texture.image = { data: pixels, width: map.width * 4, height: map.height * 4 }
  texture.needsUpdate = true
}
