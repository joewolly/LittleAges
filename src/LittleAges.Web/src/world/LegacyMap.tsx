import { useLayoutEffect, useRef } from 'react'
import type { Citizen, Map, Structure } from '../api'
import { containMap } from './visuals'

const terrainColors: Record<number, string> = { 1: '#89b8c5', 2: '#d3c78e', 3: '#76966a', 4: '#968873', 5: '#4f785b' }
const structureColors: Record<Structure['type'], string> = { Shelter: '#c76848', Stockpile: '#805c3d', Workshop: '#75569a', Farm: '#b9a134', Granary: '#b77938' }

export function LegacyMap({ map, structures, citizens }: { map: Map; structures: Structure[]; citizens: Citizen[] }) {
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
      const { scale, offsetX, offsetY } = containMap(width, height, map.width, map.height)
      context.fillStyle = '#f7f0e5'
      context.fillRect(0, 0, width, height)
      for (let y = 0; y < map.height; y += 1) for (let x = 0; x < map.width; x += 1) {
        context.fillStyle = terrainColors[map.terrain[y * map.width + x]]
        context.fillRect(offsetX + x * scale, offsetY + y * scale, scale + 0.4, scale + 0.4)
      }
      context.strokeStyle = '#f7f0e5'
      context.lineWidth = Math.max(1, scale / 12)
      context.strokeRect(offsetX + map.startingSite.x * scale, offsetY + map.startingSite.y * scale, scale, scale)
      for (const structure of structures) {
        const x = offsetX + structure.location.x * scale
        const y = offsetY + structure.location.y * scale
        context.fillStyle = structure.type === 'Farm' ? ({ Fallow: '#735332', Planted: '#8eaa49', Growing: '#467e32', Harvest: '#e1b63f', Dormant: '#97866a' }[structure.cropStage ?? 'Fallow'] ?? '#735332') : structureColors[structure.type]
        context.globalAlpha = structure.status === 'Complete' ? 1 : 0.52
        context.fillRect(x + scale * 0.18, y + scale * 0.18, scale * 0.64, scale * 0.64)
        context.globalAlpha = 1
      }
      context.fillStyle = '#302a24'
      for (const citizen of citizens.filter(entry => entry.isAlive)) context.fillRect(offsetX + citizen.location.x * scale + scale * 0.38, offsetY + citizen.location.y * scale + scale * 0.38, Math.max(2, scale * 0.24), Math.max(2, scale * 0.24))
    }
    render()
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(render)
    observer?.observe(host)
    window.addEventListener('resize', render)
    return () => {
      observer?.disconnect()
      window.removeEventListener('resize', render)
    }
  }, [citizens, map, structures])

  return <div className="world-fallback" role="img" aria-label={`Settlement map, ${map.width} by ${map.height} tiles. Colored squares mark structures and dark points mark living citizens.`}><canvas ref={canvasRef} /></div>
}
