import type { LivingWorld } from '../living'
import { useLayoutEffect, useRef } from 'react'
import type { Citizen, Map, SettlementSite, Structure } from '../api'
import { containMap } from './visuals'

const terrainColors: Record<number, string> = { 1: '#89b8c5', 2: '#d3c78e', 3: '#76966a', 4: '#968873', 5: '#4f785b' }
const structureColors: Record<Structure['type'], string> = { Shelter: '#c76848', Stockpile: '#805c3d', Workshop: '#75569a', Farm: '#b9a134', Granary: '#b77938', Marketplace: '#bd5175' }

export function LegacyMap({ map, structures, citizens, living, focusSettlement = false, settlementSites = [], selectedSettlementId = null, focusedSettlementId = null }: { living?: LivingWorld | null; focusSettlement?: boolean; map: Map; structures: Structure[]; citizens: Citizen[]; settlementSites?: SettlementSite[]; selectedSettlementId?: string | null; focusedSettlementId?: string | null }) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const focusedSite = settlementSites.find(site => site.settlementId === focusedSettlementId) ?? null
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
      const minX = focusedSite ? Math.max(0, focusedSite.site.x - 8) : focusSettlement ? Math.max(0, Math.min(...sites.map(s => s.x)) - 8) : 0
      const minY = focusedSite ? Math.max(0, focusedSite.site.y - 8) : focusSettlement ? Math.max(0, Math.min(...sites.map(s => s.y)) - 8) : 0
      const maxX = focusedSite ? Math.min(map.width, focusedSite.site.x + 9) : focusSettlement ? Math.min(map.width, Math.max(...sites.map(s => s.x)) + 9) : map.width
      const maxY = focusedSite ? Math.min(map.height, focusedSite.site.y + 9) : focusSettlement ? Math.min(map.height, Math.max(...sites.map(s => s.y)) + 9) : map.height
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
        context.fillStyle = structure.type === 'Farm' ? ({ Fallow: '#735332', Planted: '#8eaa49', Growing: '#467e32', Harvest: '#e1b63f', Dormant: '#97866a' }[structure.cropStage ?? 'Fallow'] ?? '#735332') : structureColors[structure.type]
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
      for (const citizen of citizens.filter(entry => entry.isAlive)) {
        context.fillStyle = citizen.currentAction === 'WorkFarm' ? '#d3ef9a' : citizen.currentAction === 'HaulHarvest' ? '#ffe375' : citizen.currentAction === 'TradeDelivery' ? '#f54aa1' : '#302a24'
        context.fillRect(offsetX + citizen.location.x * scale + scale * 0.38, offsetY + citizen.location.y * scale + scale * 0.38, Math.max(2, scale * 0.24), Math.max(2, scale * 0.24))
      }
      if (settlementSites.length > 1) for (const settlement of settlementSites) {
        const centerX = offsetX + (settlement.site.x + 0.5) * scale
        const centerY = offsetY + (settlement.site.y + 0.5) * scale
        context.beginPath()
        context.arc(centerX, centerY, Math.max(2, Math.min(6, scale * 0.35)), 0, Math.PI * 2)
        context.fillStyle = settlement.settlementId === selectedSettlementId ? '#f3d287' : '#523f31'
        context.fill()
        context.lineWidth = Math.max(1, scale / 10)
        context.strokeStyle = '#fff8e9'
        context.stroke()
      }
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
  }, [citizens, map, structures, living, focusSettlement, settlementSites, selectedSettlementId, focusedSite])

  const siteDescription = settlementSites.length > 1 ? ` ${settlementSites.length} settlement sites are marked` : ''
  const focusDescription = focusedSite ? `, focused on settlement ${focusedSite.settlementId} at (${focusedSite.site.x}, ${focusedSite.site.y})` : focusSettlement ? ', focused on the settlement' : `, ${map.width} by ${map.height} tiles`
  return <div className="world-fallback" role="img" aria-label={`Settlement map${focusDescription}.${siteDescription} Colored squares mark structures and fields; gold outlines mark active work. Citizen points: green farming, gold harvest hauling, pink market trips, dark other activity.`}><canvas ref={canvasRef} /><span className="world-fallback-legend">Citizen activity: <b className="farm-activity">■</b> farming · <b className="harvest-activity">■</b> harvest · <b className="market-activity">■</b> market</span></div>
}
