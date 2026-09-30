import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import type { Citizen, Map as WorldMap, RoadOverlay, Settlement, SettlementSite, Structure } from '../api'
import type { LivingOrder, LivingWorld } from '../living'
import { PresentationClock, doorway, doorwayPlan, restingHome } from './presentation'
import { ROAD_STYLES, roadSegments } from './roads'
import { citizenPaletteIndex, worldPointAlongMovementPlan } from './visuals'
import { clampCamera, homeZoom, panCamera, screenToWorld, visibleTiles, worldToScreen, zoomCameraAt, type IsoCamera } from './iso/projection'
import { animalSprites, buildStaticScene, cameraFocus, siteSprites, sortByDepth, type SceneSprite } from './iso/scene'
import { TERRAIN_PALETTES, seasonAt, type Season } from './iso/seasons'
import { SpriteKit, spriteKeysFor, spritePlacement, zoomBucket } from './iso/sprites'
import { CHUNK_TILES, drawTerrainChunk, type TerrainChunk } from './iso/terrain'
import { shelterTier } from './iso/tiers'

export type CameraNudge = { x: number; z: number; zoom: number; sequence: number }
export type IsoStats = { fps: number; sprites: number; chunks: number; p95: number; tier: number; season: string }

export type IsoWorldProps = {
  map: WorldMap
  living?: LivingWorld | null
  roads?: RoadOverlay | null
  citizens: Citizen[]
  structures: Structure[]
  settlement: Settlement | null
  settlementSites: SettlementSite[]
  focusedSettlementId: string | null
  onFocusSettlementSite: (settlementId: string) => void
  worldSeed: string | null
  worldMinute: number
  operationalSpeed: number | null
  paused: boolean
  reducedMotion: boolean
  controlsEnabled: boolean
  selectedCitizenId: string | null
  onSelectCitizen: (citizenId: string) => void
  resetToken: number
  nudge: CameraNudge
  followCitizenId: string | null
  onStats?: (stats: IsoStats) => void
  /** Development art review only: show this season instead of the calendar's. */
  previewSeason?: Season
}

const MAX_DEVICE_PIXEL_RATIO = 1.5
const MAX_CACHED_CHUNKS = 64
const FOOD_GOODS = new Set(['Food', 'Meal', 'Grain', 'PreservedFood'])
const WOOD_GOODS = new Set(['Wood', 'Fuel'])

function carriedSprite(citizen: Citizen, order: LivingOrder | undefined): string {
  const carried = citizen.carriedResource ?? (order?.cargoInTransit ? order.cargo[0]?.good : order?.phase === 'Travel' && !order.suppliesDelivered ? order.ingredients[0]?.resource : null)
  if (!carried) return 'empty'
  if (FOOD_GOODS.has(carried)) return 'food'
  if (WOOD_GOODS.has(carried)) return 'wood'
  return carried === 'Stone' ? 'stone' : 'empty'
}

function lifeStageScale(citizen: Citizen): number {
  switch (citizen.lifeStage) {
    case 'YoungChild': case 'Young Child': return 0.6
    case 'Child': return 0.72
    case 'Adolescent': return 0.88
    default: return 1
  }
}

type CitizenPose = { x: number; y: number; visible: boolean }

/**
 * The painted 2D settlement. It only draws server observations: every position
 * comes from canonical locations and movement plans, and cosmetic variety comes
 * from stable hashes of the world seed and entity IDs.
 */
export function IsoWorld(props: IsoWorldProps) {
  const hostRef = useRef<HTMLDivElement>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const propsRef = useRef(props)
  const cameraRef = useRef<IsoCamera>({ x: props.map.startingSite.x, y: props.map.startingSite.y, zoom: 24 })
  const sizeRef = useRef({ width: 1, height: 1, ratio: 1 })
  const posesRef = useRef(new Map<string, CitizenPose>())
  const chunksRef = useRef(new Map<string, TerrainChunk | null>())
  const dirtyRef = useRef(true)
  const [kit] = useState(() => new SpriteKit())
  const [clock] = useState(() => new PresentationClock())
  const season = props.previewSeason ?? seasonAt(props.worldMinute)
  const tier = shelterTier(props.living, props.structures, props.citizens)

  const staticScene = useMemo(() => buildStaticScene({ map: props.map, season, worldSeed: props.worldSeed, structures: props.structures, settlement: props.settlement, living: props.living, tier }),
    [props.map, season, props.worldSeed, props.structures, props.settlement, props.living, tier])
  const sceneRef = useRef(staticScene)
  const focusedSite = props.settlementSites.find(site => site.settlementId === props.focusedSettlementId) ?? null
  const focus = useMemo(() => cameraFocus(props.map, props.structures, focusedSite), [props.map, props.structures, focusedSite])

  useLayoutEffect(() => {
    propsRef.current = props
    sceneRef.current = staticScene
    dirtyRef.current = true
  })
  useLayoutEffect(() => { clock.observe(props.worldMinute, props.operationalSpeed, props.paused, performance.now()) }, [clock, props.worldMinute, props.operationalSpeed, props.paused])
  useEffect(() => { kit.load(spriteKeysFor(season)) }, [kit, season])
  useEffect(() => kit.onChange(() => { dirtyRef.current = true }), [kit])
  useEffect(() => { chunksRef.current.clear(); dirtyRef.current = true }, [props.map, season, props.worldSeed])

  // Home view: on first layout, on Reset, and when a settlement site is focused.
  const focusX = focus.x
  const focusY = focus.y
  useLayoutEffect(() => {
    const width = hostRef.current?.getBoundingClientRect().width || 1024
    cameraRef.current = clampCamera({ x: focusX, y: focusY, zoom: homeZoom(width) }, props.map.width, props.map.height)
    dirtyRef.current = true
  }, [focusX, focusY, props.resetToken, props.map.width, props.map.height])

  const nudgeSequence = props.nudge.sequence
  useEffect(() => {
    if (nudgeSequence === 0) return
    const { x, z, zoom } = propsRef.current.nudge
    const camera = cameraRef.current
    cameraRef.current = clampCamera({ x: camera.x + x, y: camera.y + z, zoom: camera.zoom * (zoom > 0 ? 1.2 : zoom < 0 ? 1 / 1.2 : 1) }, propsRef.current.map.width, propsRef.current.map.height)
    dirtyRef.current = true
  }, [nudgeSequence])

  useEffect(() => {
    const host = hostRef.current
    const canvas = canvasRef.current
    if (!host || !canvas) return
    const context = canvas.getContext('2d')
    if (!context) return
    let frame = 0
    let last = performance.now()
    const samples: number[] = []
    let statsAt = last
    let drawnSprites = 0

    const resize = () => {
      const bounds = host.getBoundingClientRect()
      const ratio = Math.min(window.devicePixelRatio || 1, MAX_DEVICE_PIXEL_RATIO)
      sizeRef.current = { width: Math.max(1, bounds.width), height: Math.max(1, bounds.height), ratio }
      canvas.width = Math.round(sizeRef.current.width * ratio)
      canvas.height = Math.round(sizeRef.current.height * ratio)
      dirtyRef.current = true
    }
    resize()
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(resize)
    observer?.observe(host)

    const orders = () => new Map((propsRef.current.living?.orders ?? []).filter(order => order.citizenId !== null).map(order => [order.citizenId as string, order]))

    /** Advances every villager toward its canonical position; returns true while anyone is still moving. */
    const stepCitizens = (now: number, delta: number): boolean => {
      const current = propsRef.current
      const poses = posesRef.current
      const alive = current.citizens.filter(citizen => citizen.isAlive)
      const minute = clock.at(now)
      const routeMotion = !current.reducedMotion && current.operationalSpeed !== null && current.operationalSpeed > 0 && current.operationalSpeed <= 10
      let moving = false
      const seen = new Set<string>()
      for (const citizen of alive) {
        seen.add(citizen.citizenId)
        const home = restingHome(citizen, current.structures)
        const plan = routeMotion && !current.paused ? doorwayPlan(citizen, current.structures) : null
        let target = plan ? worldPointAlongMovementPlan(plan, minute, current.operationalSpeed) : { ...citizen.location }
        if (home) target = doorway(home)
        const pose = poses.get(citizen.citizenId)
        if (!pose || current.reducedMotion) {
          poses.set(citizen.citizenId, { x: target.x, y: target.y, visible: home === null })
          continue
        }
        if (!pose.visible && !home) {
          // Leaving home: step out of the front door rather than appearing mid-route.
          const house = current.structures.find(structure => structure.structureId === citizen.homeStructureId)
          const exit = house ? doorway(house) : target
          pose.x = exit.x
          pose.y = exit.y
          pose.visible = true
        }
        const blend = 1 - Math.exp(-Math.min(delta, 0.1) * (plan ? 18 : 12))
        const dx = target.x - pose.x
        const dy = target.y - pose.y
        if (dx * dx + dy * dy > 0.00001) { pose.x += dx * blend; pose.y += dy * blend; moving = true }
        if (home && dx * dx + dy * dy < 0.01) pose.visible = false
      }
      for (const id of [...poses.keys()]) if (!seen.has(id)) poses.delete(id)
      return moving
    }

    const draw = (now: number) => {
      const current = propsRef.current
      const { width, height, ratio } = sizeRef.current
      const camera = cameraRef.current
      const seasonName = current.previewSeason ?? seasonAt(current.worldMinute)
      const palette = TERRAIN_PALETTES[seasonName]
      const bucket = zoomBucket(camera.zoom * ratio)
      context.setTransform(ratio, 0, 0, ratio, 0, 0)
      context.fillStyle = palette.backdrop
      context.fillRect(0, 0, width, height)
      const bounds = visibleTiles(camera, width, height, 3)
      const project = (x: number, y: number, lift = 0) => worldToScreen(camera, width, height, x, y, lift)

      // Ground: cached terrain chunks.
      const chunks = chunksRef.current
      let chunkCount = 0
      for (let cy = Math.max(0, Math.floor(bounds.minY / CHUNK_TILES)); cy <= Math.min(Math.ceil(current.map.height / CHUNK_TILES) - 1, Math.floor(bounds.maxY / CHUNK_TILES)); cy += 1) {
        for (let cx = Math.max(0, Math.floor(bounds.minX / CHUNK_TILES)); cx <= Math.min(Math.ceil(current.map.width / CHUNK_TILES) - 1, Math.floor(bounds.maxX / CHUNK_TILES)); cx += 1) {
          const key = `${cx}:${cy}:${bucket}:${seasonName}`
          let chunk = chunks.get(key)
          if (chunk === undefined) {
            chunk = drawTerrainChunk(current.map, palette, cx, cy, bucket, current.worldSeed ?? 'world')
            chunks.set(key, chunk)
            if (chunks.size > MAX_CACHED_CHUNKS) chunks.delete(chunks.keys().next().value as string)
          }
          if (!chunk) continue
          // Chunk origins are in projection units, measured like the camera centre.
          const left = width / 2 + (chunk.originX - (camera.x - camera.y)) * camera.zoom
          const top = height / 2 + (chunk.originY - (camera.x + camera.y) / 2) * camera.zoom
          context.drawImage(chunk.canvas, left, top, chunk.canvas.width / bucket * camera.zoom, chunk.canvas.height / bucket * camera.zoom)
          chunkCount += 1
        }
      }

      // Roads, graded like the records map: tan tracks, brown trails, pale paved roads.
      if (current.roads && current.roads.tiles.length > 0) {
        context.lineCap = 'round'
        for (const segment of roadSegments(current.roads.tiles)) {
          const style = ROAD_STYLES[segment.grade]
          const from = project(segment.from.x, segment.from.y)
          const to = project(segment.to.x, segment.to.y)
          const widthPx = Math.max(1.5, style.width * camera.zoom * 1.4)
          if (segment.grade === 'Road') {
            context.strokeStyle = '#b3a68c'
            context.lineWidth = widthPx + Math.max(2, camera.zoom * 0.12)
            context.setLineDash([])
            context.beginPath(); context.moveTo(from.x, from.y); context.lineTo(to.x, to.y); context.stroke()
          }
          context.strokeStyle = segment.grade === 'Road' ? '#e4dac6' : segment.grade === 'Trail' ? '#c99b5f' : '#d4b27c'
          context.lineWidth = widthPx
          context.setLineDash(style.dashed ? [camera.zoom * 0.3, camera.zoom * 0.25] : [])
          context.beginPath(); context.moveTo(from.x, from.y); context.lineTo(to.x, to.y); context.stroke()
        }
        context.setLineDash([])
      }

      // Active work sites: a soft gold diamond on the ground.
      context.strokeStyle = 'rgba(255, 236, 170, 0.85)'
      context.lineWidth = Math.max(1, camera.zoom * 0.06)
      for (const order of (current.living?.orders ?? []).filter(o => o.citizenId !== null).slice(0, 64)) {
        const { x, y } = order.location
        if (x < bounds.minX || x > bounds.maxX || y < bounds.minY || y > bounds.maxY) continue
        const c = project(x, y)
        context.beginPath()
        context.moveTo(c.x, c.y - camera.zoom * 0.42); context.lineTo(c.x + camera.zoom * 0.84, c.y); context.lineTo(c.x, c.y + camera.zoom * 0.42); context.lineTo(c.x - camera.zoom * 0.84, c.y); context.closePath()
        context.stroke()
      }

      // Everything standing: depth-sorted sprites from visible chunks plus people and animals.
      const visible: SceneSprite[] = []
      const scene = sceneRef.current
      for (let cy = Math.floor(bounds.minY / CHUNK_TILES); cy <= Math.floor(bounds.maxY / CHUNK_TILES); cy += 1) {
        for (let cx = Math.floor(bounds.minX / CHUNK_TILES); cx <= Math.floor(bounds.maxX / CHUNK_TILES); cx += 1) {
          for (const sprite of scene.get(`${cx}:${cy}`) ?? []) if (sprite.x >= bounds.minX && sprite.x <= bounds.maxX && sprite.y >= bounds.minY && sprite.y <= bounds.maxY) visible.push(sprite)
        }
      }
      visible.push(...siteSprites(current.settlementSites, current.focusedSettlementId), ...animalSprites(current.living))
      const livingOrders = orders()
      const selected = current.selectedCitizenId
      let selectedPose: CitizenPose | null = null
      for (const citizen of current.citizens) {
        if (!citizen.isAlive) continue
        const pose = posesRef.current.get(citizen.citizenId)
        if (!pose || !pose.visible) continue
        if (citizen.citizenId === selected) selectedPose = pose
        const key = `people/villager-${citizenPaletteIndex(current.worldSeed, citizen.citizenId)}-${carriedSprite(citizen, livingOrders.get(citizen.citizenId))}`
        visible.push({ key, x: pose.x, y: pose.y, scale: lifeStageScale(citizen), depth: pose.x + pose.y + 0.3 })
      }
      if (selectedPose) {
        const c = project(selectedPose.x, selectedPose.y)
        context.fillStyle = 'rgba(255, 246, 200, 0.45)'
        context.strokeStyle = '#fff6c8'
        context.lineWidth = Math.max(1.5, camera.zoom * 0.08)
        context.beginPath(); context.ellipse(c.x, c.y, camera.zoom * 0.55, camera.zoom * 0.28, 0, 0, Math.PI * 2); context.fill(); context.stroke()
      }
      sortByDepth(visible)
      drawnSprites = 0
      for (const sprite of visible) {
        const placement = spritePlacement(sprite.key)
        const raster = placement ? kit.raster(sprite.key, bucket) : null
        if (!placement || !raster) continue
        const anchor = project(sprite.x, sprite.y)
        const unit = camera.zoom * sprite.scale
        context.drawImage(raster, anchor.x - placement.anchorX * unit, anchor.y - placement.anchorY * unit, placement.width * unit, placement.height * unit)
        drawnSprites += 1
      }
      if (selectedPose) {
        const top = project(selectedPose.x, selectedPose.y, 1.35)
        const size = Math.max(8, camera.zoom * 0.28)
        context.fillStyle = '#f5c451'
        context.strokeStyle = '#2a1a0e'
        context.lineWidth = Math.max(1.5, size * 0.18)
        context.lineJoin = 'round'
        context.beginPath(); context.moveTo(top.x - size, top.y - size * 1.2); context.lineTo(top.x + size, top.y - size * 1.2); context.lineTo(top.x, top.y); context.closePath(); context.fill(); context.stroke()
      }

      // Weather is a light screen overlay; it never hides the map.
      const weather = current.living?.weather
      if (weather === 'ColdSpell') { context.fillStyle = 'rgba(190, 215, 235, 0.18)'; context.fillRect(0, 0, width, height) }
      if (weather === 'Rain') {
        context.fillStyle = 'rgba(60, 80, 100, 0.12)'
        context.fillRect(0, 0, width, height)
        context.strokeStyle = 'rgba(210, 230, 245, 0.55)'
        context.lineWidth = 1.2
        const drift = current.reducedMotion ? 0 : (now / 6) % 60
        context.beginPath()
        for (let i = 0; i < 90; i += 1) {
          const x = (i * 97) % width
          const y = ((i * 53) % height + drift * 4) % height
          context.moveTo(x, y); context.lineTo(x - 4, y + 12)
        }
        context.stroke()
      }

      const frameMs = now - last
      samples.push(frameMs)
      if (now - statsAt > 1000 && current.onStats) {
        const sorted = [...samples].sort((a, b) => a - b)
        current.onStats({ fps: Math.round(samples.length * 1000 / (now - statsAt)), sprites: drawnSprites, chunks: chunkCount, p95: sorted[Math.floor(sorted.length * 0.95)] ?? 0, tier: shelterTier(current.living, current.structures, current.citizens), season: seasonName })
        samples.length = 0
        statsAt = now
      }
    }

    const tick = (now: number) => {
      const delta = (now - last) / 1000
      const current = propsRef.current
      const moving = stepCitizens(now, delta)
      const follow = current.followCitizenId ? posesRef.current.get(current.followCitizenId) : undefined
      if (follow) {
        const camera = cameraRef.current
        const blend = 1 - Math.exp(-Math.min(delta, 0.1) * 5)
        const dx = follow.x - camera.x
        const dy = follow.y - camera.y
        if (dx * dx + dy * dy > 0.0001) { cameraRef.current = { ...camera, x: camera.x + dx * blend, y: camera.y + dy * blend }; dirtyRef.current = true }
      }
      if (moving || dirtyRef.current || current.living?.weather === 'Rain' && !current.reducedMotion) {
        draw(now)
        dirtyRef.current = false
      }
      last = now
      frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => { cancelAnimationFrame(frame); observer?.disconnect() }
  }, [clock, kit])

  // Pointer input: drag to pan, wheel or pinch to zoom, tap to select a villager or a settlement site.
  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const pointers = new Map<number, { x: number; y: number }>()
    let dragDistance = 0
    let pinchDistance = 0
    const local = (event: PointerEvent | WheelEvent) => { const rect = canvas.getBoundingClientRect(); return { x: event.clientX - rect.left, y: event.clientY - rect.top } }
    const enabled = () => propsRef.current.controlsEnabled
    const setCamera = (camera: IsoCamera) => { cameraRef.current = clampCamera(camera, propsRef.current.map.width, propsRef.current.map.height); dirtyRef.current = true }

    const onWheel = (event: WheelEvent) => {
      if (!enabled()) return
      event.preventDefault()
      const point = local(event)
      const { width, height } = sizeRef.current
      setCamera(zoomCameraAt(cameraRef.current, width, height, point.x, point.y, Math.pow(1.0015, -event.deltaY)))
    }
    const onDown = (event: PointerEvent) => {
      if (!enabled()) return
      canvas.setPointerCapture(event.pointerId)
      pointers.set(event.pointerId, local(event))
      dragDistance = 0
      if (pointers.size === 2) { const [a, b] = [...pointers.values()]; pinchDistance = Math.hypot(a.x - b.x, a.y - b.y) }
    }
    const onMove = (event: PointerEvent) => {
      const previous = pointers.get(event.pointerId)
      if (!previous || !enabled()) return
      const point = local(event)
      pointers.set(event.pointerId, point)
      if (pointers.size === 1) {
        dragDistance += Math.hypot(point.x - previous.x, point.y - previous.y)
        setCamera(panCamera(cameraRef.current, point.x - previous.x, point.y - previous.y))
      } else if (pointers.size === 2) {
        const [a, b] = [...pointers.values()]
        const distance = Math.hypot(a.x - b.x, a.y - b.y)
        const { width, height } = sizeRef.current
        if (pinchDistance > 0) setCamera(zoomCameraAt(cameraRef.current, width, height, (a.x + b.x) / 2, (a.y + b.y) / 2, distance / pinchDistance))
        pinchDistance = distance
        dragDistance = Infinity
      }
    }
    const onUp = (event: PointerEvent) => {
      const point = pointers.get(event.pointerId)
      pointers.delete(event.pointerId)
      if (pointers.size < 2) pinchDistance = 0
      if (!point || dragDistance > 6 || !enabled()) return
      const current = propsRef.current
      const { width, height } = sizeRef.current
      const camera = cameraRef.current
      let best: { id: string; distance: number } | null = null
      for (const citizen of current.citizens) {
        const pose = posesRef.current.get(citizen.citizenId)
        if (!citizen.isAlive || !pose?.visible) continue
        const body = worldToScreen(camera, width, height, pose.x, pose.y, 0.45)
        const distance = Math.hypot(body.x - point.x, body.y - point.y)
        if (distance < Math.max(16, camera.zoom * 0.6) && (!best || distance < best.distance)) best = { id: citizen.citizenId, distance }
      }
      if (best) { current.onSelectCitizen(best.id); return }
      const tile = screenToWorld(camera, width, height, point.x, point.y)
      const site = current.settlementSites.length > 1 ? current.settlementSites.find(s => Math.abs(s.site.x - tile.x) < 0.8 && Math.abs(s.site.y - tile.y) < 0.8) : undefined
      if (site) current.onFocusSettlementSite(site.settlementId)
    }
    canvas.addEventListener('wheel', onWheel, { passive: false })
    canvas.addEventListener('pointerdown', onDown)
    canvas.addEventListener('pointermove', onMove)
    canvas.addEventListener('pointerup', onUp)
    canvas.addEventListener('pointercancel', onUp)
    return () => {
      canvas.removeEventListener('wheel', onWheel)
      canvas.removeEventListener('pointerdown', onDown)
      canvas.removeEventListener('pointermove', onMove)
      canvas.removeEventListener('pointerup', onUp)
      canvas.removeEventListener('pointercancel', onUp)
    }
  }, [])

  return <div ref={hostRef} className="iso-world" data-season={season} data-shelter-tier={tier}>
    <canvas ref={canvasRef} aria-hidden="true" />
  </div>
}
