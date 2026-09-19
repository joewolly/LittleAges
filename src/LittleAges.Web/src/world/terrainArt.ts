import * as THREE from 'three'
import type { Map } from '../api'
import { stableVisualHash } from './visuals'

const palette = ['#299cad', '#daca89', '#79af39', '#a9a58c', '#5b9633'].map(color => new THREE.Color(color))

/** Blends presentation colors only. Tile identity, heights and routes stay authoritative. */
export function createGroundTexture(map: Map, seed: string | null): THREE.DataTexture {
  const resolution = 4
  const width = map.width * resolution, height = map.height * resolution
  const pixels = new Uint8Array(width * height * 4)
  const color = new THREE.Color()
  for (let py = 0; py < height; py++) for (let px = 0; px < width; px++) {
    const x = (px + 0.5) / resolution - 0.5, y = (py + 0.5) / resolution - 0.5
    const ix = Math.floor(x), iy = Math.floor(y)
    const fx = x - ix, fy = y - iy
    color.setRGB(0, 0, 0)
    for (let dy = 0; dy <= 1; dy++) for (let dx = 0; dx <= 1; dx++) {
      const sx = Math.max(0, Math.min(map.width - 1, ix + dx)), sy = Math.max(0, Math.min(map.height - 1, iy + dy))
      const sample = palette[map.terrain[sy * map.width + sx] - 1] ?? palette[2]
      const weight = (dx ? fx : 1 - fx) * (dy ? fy : 1 - fy)
      color.r += sample.r * weight; color.g += sample.g * weight; color.b += sample.b * weight
    }
    const noise = (stableVisualHash(seed ?? 'world', 'ground-paint', px, py) & 255) / 255
    color.multiplyScalar(0.96 + noise * 0.08).convertLinearToSRGB()
    const offset = (py * width + px) * 4
    pixels[offset] = Math.round(Math.min(1, color.r) * 255)
    pixels[offset + 1] = Math.round(Math.min(1, color.g) * 255)
    pixels[offset + 2] = Math.round(Math.min(1, color.b) * 255)
    pixels[offset + 3] = 255
  }
  const texture = new THREE.DataTexture(pixels, width, height)
  texture.colorSpace = THREE.SRGBColorSpace
  texture.magFilter = THREE.LinearFilter
  texture.minFilter = THREE.LinearMipmapLinearFilter
  texture.generateMipmaps = true
  texture.needsUpdate = true
  return texture
}
