import { expect, it } from 'vitest'
import type { Map } from '../api'
import { createGroundTexture, upgradeGroundTexture } from './terrainArt'
import { groundPixels } from './terrainPixels'

it('releases the small GPU allocation before upgrading the terrain image dimensions', () => {
  const map: Map = { width: 2, height: 2, terrain: [1, 2, 3, 4], elevation: [0, 0, 0, 0], resources: [], startingSite: { x: 0, y: 0 } }
  const texture = createGroundTexture(map, '42')
  const disposedDimensions: number[][] = []
  texture.addEventListener('dispose', () => disposedDimensions.push([texture.image.width, texture.image.height]))
  const pixels = groundPixels(map, '42')
  const version = texture.version
  upgradeGroundTexture(texture, map, pixels)
  expect(disposedDimensions).toEqual([[2, 2]])
  expect(texture.image).toEqual({ data: pixels, width: 8, height: 8 })
  expect(texture.version).toBeGreaterThan(version)
  texture.dispose()
  expect(disposedDimensions).toEqual([[2, 2], [8, 8]])
})
