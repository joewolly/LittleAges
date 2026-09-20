import { expect, it, vi } from 'vitest'
import { BoxGeometry, Group, Mesh, MeshStandardMaterial, Texture, type WebGLRenderer } from 'three'
import { disposeRendererResources, trackAssetResources, trackLightingUniform } from './rendererResources'

it('releases shared model GPU resources once while retaining decoded assets for retry', () => {
  const renderer = {} as WebGLRenderer
  const texture = new Texture({ width: 1, height: 1 })
  const geometry = new BoxGeometry()
  const material = new MeshStandardMaterial({ map: texture, roughnessMap: texture })
  const scene = new Group()
  scene.add(new Mesh(geometry, material), new Mesh(geometry, material))
  const disposed = [texture, geometry, material].map(resource => {
    const listener = vi.fn()
    resource.addEventListener('dispose', listener)
    return listener
  })
  trackAssetResources(renderer, scene)
  trackAssetResources(renderer, scene)
  disposeRendererResources(renderer)
  disposeRendererResources(renderer)
  for (const listener of disposed) expect(listener).toHaveBeenCalledTimes(1)
  expect(scene.children).toHaveLength(2)
  expect(texture.image).toEqual({ width: 1, height: 1 })
  expect(geometry.getAttribute('position').count).toBeGreaterThan(0)

  trackAssetResources(renderer, scene)
  disposeRendererResources(renderer)
  for (const listener of disposed) expect(listener).toHaveBeenCalledTimes(2)
})

it('releases the shared lighting texture assigned after shader compilation', () => {
  const renderer = {} as WebGLRenderer
  const uniform: { value: Texture | null } = { value: null }
  trackLightingUniform(renderer, uniform)
  const texture = new Texture()
  const disposed = vi.fn()
  texture.addEventListener('dispose', disposed)
  uniform.value = texture
  disposeRendererResources(renderer)
  expect(disposed).toHaveBeenCalledOnce()
})

it('does not retire another renderer resource registry', () => {
  const first = {} as WebGLRenderer
  const second = {} as WebGLRenderer
  const texture = new Texture()
  const disposed = vi.fn()
  texture.addEventListener('dispose', disposed)
  trackLightingUniform(second, { value: texture })
  disposeRendererResources(first)
  expect(disposed).not.toHaveBeenCalled()
  disposeRendererResources(second)
  expect(disposed).toHaveBeenCalledOnce()
})
