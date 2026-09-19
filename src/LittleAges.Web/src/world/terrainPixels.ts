import type { Map } from '../api'
import { stableVisualHash } from './visuals'

const linear = (value: number) => value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
const srgb = (value: number) => value <= 0.0031308 ? value * 12.92 : 1.055 * value ** (1 / 2.4) - 0.055
const palette = [0x299cad, 0xdaca89, 0x79af39, 0xa9a58c, 0x5b9633].map(rgb => [16, 8, 0].map(shift => linear(((rgb >> shift) & 255) / 255)))

export function groundPixels(map: Pick<Map, 'width' | 'height' | 'terrain'>, seed: string | null, resolution = 4): Uint8Array<ArrayBuffer> {
  const width = map.width * resolution, height = map.height * resolution
  const pixels = new Uint8Array(width * height * 4)
  for (let py = 0; py < height; py++) for (let px = 0; px < width; px++) {
    const x = (px + 0.5) / resolution - 0.5, y = (py + 0.5) / resolution - 0.5
    const ix = Math.floor(x), iy = Math.floor(y), fx = x - ix, fy = y - iy
    let r = 0, g = 0, b = 0
    for (let dy = 0; dy <= 1; dy++) for (let dx = 0; dx <= 1; dx++) {
      const sx = Math.max(0, Math.min(map.width - 1, ix + dx)), sy = Math.max(0, Math.min(map.height - 1, iy + dy))
      const sample = palette[map.terrain[sy * map.width + sx] - 1] ?? palette[2]
      const weight = (dx ? fx : 1 - fx) * (dy ? fy : 1 - fy)
      r += sample[0] * weight; g += sample[1] * weight; b += sample[2] * weight
    }
    const noise = 0.96 + (stableVisualHash(seed ?? 'world', 'ground-paint', px, py) & 255) / 255 * 0.08
    const offset = (py * width + px) * 4
    pixels[offset] = Math.round(Math.min(1, srgb(r * noise)) * 255)
    pixels[offset + 1] = Math.round(Math.min(1, srgb(g * noise)) * 255)
    pixels[offset + 2] = Math.round(Math.min(1, srgb(b * noise)) * 255)
    pixels[offset + 3] = 255
  }
  return pixels
}
