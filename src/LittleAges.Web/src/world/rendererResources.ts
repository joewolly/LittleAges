import { Mesh, Texture, type IUniform, type Object3D, type WebGLRenderer } from 'three'

type Disposable = { dispose: () => void }
type Resources = { assets: Set<Object3D>; values: Set<Disposable>; uniforms: Set<IUniform> }
const renderers = new WeakMap<WebGLRenderer, Resources>()

function resources(renderer: WebGLRenderer): Resources {
  let result = renderers.get(renderer)
  if (!result) {
    result = { assets: new Set(), values: new Set(), uniforms: new Set() }
    renderers.set(renderer, result)
  }
  return result
}

// GLTF's CPU cache outlives a Canvas. Release its GPU allocations when that
// renderer retires, while retaining the decoded assets for a later 3D retry.
export function trackAssetResources(renderer: WebGLRenderer, scene: Object3D) {
  const tracked = resources(renderer)
  if (tracked.assets.has(scene)) return
  tracked.assets.add(scene)
  scene.traverse(object => {
    if (!(object instanceof Mesh)) return
    tracked.values.add(object.geometry)
    for (const material of Array.isArray(object.material) ? object.material : [object.material]) {
      tracked.values.add(material)
      for (const value of Object.values(material)) if (value instanceof Texture) tracked.values.add(value)
    }
  })
}

// Three r186 assigns its shared DFG lookup texture after onBeforeCompile.
// Keep the uniform reference so teardown also releases that renderer's LUT.
export function trackLightingUniform(renderer: WebGLRenderer, uniform?: IUniform) {
  if (uniform) resources(renderer).uniforms.add(uniform)
}

export function disposeRendererResources(renderer: WebGLRenderer) {
  const tracked = renderers.get(renderer)
  if (!tracked) return
  renderers.delete(renderer)
  for (const uniform of tracked.uniforms) if (uniform.value instanceof Texture) tracked.values.add(uniform.value)
  for (const value of tracked.values) value.dispose()
}
