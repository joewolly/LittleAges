import { describe, expect, it, vi } from 'vitest'

const signalRMock = vi.hoisted(() => {
  const connection = {
    start: vi.fn(async () => undefined),
    stop: vi.fn(async () => undefined),
    on: vi.fn(),
    onreconnecting: vi.fn(),
    onreconnected: vi.fn(),
    onclose: vi.fn(),
  }
  class FakeBuilder {
    withUrl = vi.fn().mockReturnThis()
    withAutomaticReconnect = vi.fn().mockReturnThis()
    configureLogging = vi.fn().mockReturnThis()
    build = vi.fn(() => connection)
  }
  return { connection, FakeBuilder, builder: null as FakeBuilder | null }
})

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class extends signalRMock.FakeBuilder {
    constructor() {
      super()
      signalRMock.builder = this
    }
  },
  LogLevel: { Warning: 2 },
}))

import { createWorldConnection } from './live'

describe('world live connection adapter', () => {
  it('configures the world hub with automatic reconnect', async () => {
    const live = createWorldConnection()
    expect(signalRMock.builder?.withUrl).toHaveBeenCalledWith('/hubs/world')
    expect(signalRMock.builder?.withAutomaticReconnect).toHaveBeenCalledOnce()
    expect(signalRMock.builder?.configureLogging).toHaveBeenCalledWith(2)

    await live.start()
    await live.stop()
    expect(signalRMock.connection.start).toHaveBeenCalledOnce()
    expect(signalRMock.connection.stop).toHaveBeenCalledOnce()
  })

  it('maps world and connection lifecycle callbacks without interpreting payloads', () => {
    const live = createWorldConnection()
    const worldChanged = vi.fn()
    const reconnecting = vi.fn()
    const reconnected = vi.fn()
    const closed = vi.fn()
    live.onWorldChanged(worldChanged)
    live.onReconnecting(reconnecting)
    live.onReconnected(reconnected)
    live.onClose(closed)

    const payload = { revision: 4, worldMinute: 10, hostState: 'Running', persistenceState: 'Ready' }
    const worldHandler = signalRMock.connection.on.mock.calls.at(-1)?.[1] as (message: unknown) => void
    worldHandler(payload)
    const reconnectingHandler = signalRMock.connection.onreconnecting.mock.calls.at(-1)?.[0] as () => void
    const reconnectedHandler = signalRMock.connection.onreconnected.mock.calls.at(-1)?.[0] as () => void
    const closeHandler = signalRMock.connection.onclose.mock.calls.at(-1)?.[0] as () => void
    reconnectingHandler(); reconnectedHandler(); closeHandler()

    expect(worldChanged).toHaveBeenCalledWith(payload)
    expect(reconnecting).toHaveBeenCalledOnce()
    expect(reconnected).toHaveBeenCalledOnce()
    expect(closed).toHaveBeenCalledOnce()
  })
})
