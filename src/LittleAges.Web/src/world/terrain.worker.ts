import { groundPixels } from './terrainPixels'

self.onmessage = (event: MessageEvent<Parameters<typeof groundPixels>>) => {
  const pixels = groundPixels(...event.data)
  self.postMessage(pixels, { transfer: [pixels.buffer] })
}
