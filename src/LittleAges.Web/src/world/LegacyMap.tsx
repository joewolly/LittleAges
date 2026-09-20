import type { LivingWorld } from '../living'
import { useLayoutEffect, useRef } from 'react'
import type { Citizen, Map, Structure } from '../api'
import { containMap } from './visuals'

const terrainColors: Record<number, string> = { 1: '#89b8c5', 2: '#d3c78e', 3: '#76966a', 4: '#968873', 5: '#4f785b' }
const structureColors: Record<Structure['type'], string> = { Shelter: '#c76848', Stockpile: '#805c3d', Workshop: '#75569a' }

export function LegacyMap({ map, structures, citizens, living, focusSettlement = false }: { living?: LivingWorld | null; focusSettlement?: boolean; map: Map; structures: Structure[]; citizens: Citizen[] }) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  useLayoutEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const host = canvas.parentElement
    if (!host) return
    const render = () => {
      const bounds = host.getBoundingClientRect()
      const width = Math.max(1, Math.round(bounds.width))
      const height = Math.max(1, Math.round(bounds.height))
      const pixelRatio = Math.min(window.devicePixelRatio || 1, 1.5)
      const pixelWidth = Math.round(width * pixelRatio)
      const pixelHeight = Math.round(height * pixelRatio)
      if (canvas.width !== pixelWidth) canvas.width = pixelWidth
      if (canvas.height !== pixelHeight) canvas.height = pixelHeight
      const context = canvas.getContext('2d')
      if (!context) return
      context.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0)
      context.imageSmoothingEnabled = false
      const sites = [map.startingSite, ...structures.map(s => s.location), ...(living?.fields.map(f => f.location) ?? []), ...(living?.facilities.map(f => f.location) ?? [])]
      const minX = focusSettlement ? Math.max(0, Math.min(...sites.map(s => s.x)) - 8) : 0
      const minY = focusSettlement ? Math.max(0, Math.min(...sites.map(s => s.y)) - 8) : 0
      const maxX = focusSettlement ? Math.min(map.width, Math.max(...sites.map(s => s.x)) + 9) : map.width
      const maxY = focusSettlement ? Math.min(map.height, Math.max(...sites.map(s => s.y)) + 9) : map.height
      const layout = containMap(width, height, maxX - minX, maxY - minY)
      const { scale } = layout
      const offsetX = layout.offsetX - minX * scale
      const offsetY = layout.offsetY - minY * scale
      context.fillStyle = '#f7f0e5'
      context.fillRect(0, 0, width, height)
      context.save()
      context.beginPath()
      context.rect(layout.offsetX, layout.offsetY, (maxX - minX) * scale, (maxY - minY) * scale)
      context.clip()
      for (let y = minY; y < maxY; y += 1) for (let x = minX; x < maxX; x += 1) {
        context.fillStyle = terrainColors[map.terrain[y * map.width + x]]
        context.fillRect(offsetX + x * scale, offsetY + y * scale, scale + 0.4, scale + 0.4)
      }
      context.strokeStyle = '#f7f0e5'
      context.lineWidth = Math.max(1, scale / 12)
      context.strokeRect(offsetX + map.startingSite.x * scale, offsetY + map.startingSite.y * scale, scale, scale)
      for (const structure of structures) {
        const x = offsetX + structure.location.x * scale
        const y = offsetY + structure.location.y * scale
        context.fillStyle = structureColors[structure.type]
        context.globalAlpha = structure.status === 'Complete' ? 1 : 0.52
        context.fillRect(x + scale * 0.18, y + scale * 0.18, scale * 0.64, scale * 0.64)
        context.globalAlpha = 1
      }
      for (const field of living?.fields ?? []) {
        context.fillStyle = field.yieldRemaining > 0 ? '#d9b949' : '#6b8b43'
        context.fillRect(offsetX + field.location.x * scale, offsetY + field.location.y * scale, Math.max(2, scale), Math.max(2, scale))
      }
      for (const facility of living?.facilities ?? []) {
        context.fillStyle = '#b66b4b'
        context.fillRect(offsetX + facility.location.x * scale, offsetY + facility.location.y * scale, Math.max(2, scale), Math.max(2, scale))
      }
      for (const animal of living?.animals ?? []) {
        context.fillStyle = animal.predator ? '#6c5356' : '#b58d5e'
        context.fillRect(offsetX + animal.location.x * scale, offsetY + animal.location.y * scale, 2, 2)
      }
      for (const order of living?.orders.filter(o => o.citizenId !== null) ?? []) {
        context.strokeStyle = '#f0d38c'
        context.lineWidth = 1
        context.strokeRect(offsetX + order.location.x * scale - 1, offsetY + order.location.y * scale - 1, Math.max(3, scale + 2), Math.max(3, scale + 2))
      }
      context.fillStyle = '#302a24'
      for (const citizen of citizens.filter(entry => entry.isAlive)) context.fillRect(offsetX + citizen.location.x * scale + scale * 0.38, offsetY + citizen.location.y * scale + scale * 0.38, Math.max(2, scale * 0.24), Math.max(2, scale * 0.24))
      context.restore()
    }
    render()
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(render)
    observer?.observe(host)
    window.addEventListener('resize', render)
    return () => {
      observer?.disconnect()
      window.removeEventListener('resize', render)
    }
  }, [citizens, map, structures, living, focusSettlement])

  return <div className="world-fallback" role="img" aria-label={`Settlement map${focusSettlement ? ', focused on the settlement' : `, ${map.width} by ${map.height} tiles`}. Colored squares mark structures and fields, gold outlines mark active work, and dark points mark living citizens.`}><canvas ref={canvasRef} /></div>
}
