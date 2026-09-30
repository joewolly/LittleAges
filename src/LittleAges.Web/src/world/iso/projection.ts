/**
 * Fixed-angle isometric projection. World tile (x, y) is the tile's centre; one
 * "unit" is half a tile's on-screen width, so a tile diamond is 2 units wide and
 * 1 unit tall. `zoom` is screen pixels per unit.
 */
export type IsoCamera = { x: number; y: number; zoom: number }
export type ScreenPoint = { x: number; y: number }
export type TileBounds = { minX: number; minY: number; maxX: number; maxY: number }

export const MIN_ZOOM = 5
export const MAX_ZOOM = 96

export function toUnits(x: number, y: number, lift = 0): ScreenPoint {
  return { x: x - y, y: (x + y) / 2 - lift }
}

export function worldToScreen(camera: IsoCamera, width: number, height: number, x: number, y: number, lift = 0): ScreenPoint {
  const point = toUnits(x, y, lift)
  const center = toUnits(camera.x, camera.y)
  return { x: width / 2 + (point.x - center.x) * camera.zoom, y: height / 2 + (point.y - center.y) * camera.zoom }
}

export function screenToWorld(camera: IsoCamera, width: number, height: number, screenX: number, screenY: number): ScreenPoint {
  const center = toUnits(camera.x, camera.y)
  const u = (screenX - width / 2) / camera.zoom + center.x
  const v = (screenY - height / 2) / camera.zoom + center.y
  return { x: v + u / 2, y: v - u / 2 }
}

export function clampCamera(camera: IsoCamera, mapWidth: number, mapHeight: number): IsoCamera {
  return {
    x: Math.max(0, Math.min(mapWidth - 1, camera.x)),
    y: Math.max(0, Math.min(mapHeight - 1, camera.y)),
    zoom: Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, camera.zoom)),
  }
}

export function panCamera(camera: IsoCamera, screenDx: number, screenDy: number): IsoCamera {
  const du = -screenDx / camera.zoom
  const dv = -screenDy / camera.zoom
  return { ...camera, x: camera.x + dv + du / 2, y: camera.y + dv - du / 2 }
}

/** Zooms by `factor` while keeping the world point under (screenX, screenY) fixed. */
export function zoomCameraAt(camera: IsoCamera, width: number, height: number, screenX: number, screenY: number, factor: number): IsoCamera {
  const zoom = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, camera.zoom * factor))
  const before = screenToWorld(camera, width, height, screenX, screenY)
  const scaled = { ...camera, zoom }
  const after = screenToWorld(scaled, width, height, screenX, screenY)
  return { x: camera.x + before.x - after.x, y: camera.y + before.y - after.y, zoom }
}

/** Tiles whose centres fall inside the screen, widened by `margin` tiles for tall sprites. */
export function visibleTiles(camera: IsoCamera, width: number, height: number, margin: number): TileBounds {
  const corners = [[0, 0], [width, 0], [0, height], [width, height]].map(([x, y]) => screenToWorld(camera, width, height, x, y))
  return {
    minX: Math.floor(Math.min(...corners.map(point => point.x))) - margin,
    minY: Math.floor(Math.min(...corners.map(point => point.y))) - margin,
    maxX: Math.ceil(Math.max(...corners.map(point => point.x))) + margin,
    maxY: Math.ceil(Math.max(...corners.map(point => point.y))) + margin,
  }
}

/** A home zoom that shows roughly 7 tiles across on a phone and 16 on a wide desktop. */
export function homeZoom(width: number): number {
  const tilesAcross = Math.max(7, Math.min(16, width / 90))
  return Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, width / (2 * tilesAcross)))
}
