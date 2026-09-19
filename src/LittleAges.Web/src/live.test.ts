import { beforeEach, describe, expect, it, vi } from 'vitest'

const signalRMock = vi.hoisted(() => {
  const subscription = { dispose: vi.fn() }
  let subscriber: { next: (value: unknown) => void; error: (error: unknown) => void; complete: () => void } | null = null
  const connection = {
    state: 'Disconnected',
    start: vi.fn(async () => undefined),
    stop: vi.fn(async () => undefined),
    on: vi.fn(),
    onreconnecting: vi.fn(),
    onreconnected: vi.fn(),
    onclose: vi.fn(),
    stream: vi.fn(() => ({ subscribe: vi.fn(nextSubscriber => { subscriber = nextSubscriber; return subscription }) })),
  }
  class FakeBuilder {
    withUrl = vi.fn().mockReturnThis()
    withAutomaticReconnect = vi.fn().mockReturnThis()
    configureLogging = vi.fn().mockReturnThis()
    build = vi.fn(() => connection)
  }
  return { connection, subscription, getSubscriber: () => subscriber, setSubscriber: (value: typeof subscriber) => { subscriber = value }, FakeBuilder, builder: null as FakeBuilder | null }
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
  beforeEach(() => {
    vi.clearAllMocks()
    signalRMock.setSubscriber(null)
    signalRMock.connection.state = 'Disconnected'
  })

  it('configures the world hub with automatic reconnect', async () => {
    const live = createWorldConnection()
    expect(signalRMock.builder?.withUrl).toHaveBeenCalledWith('/hubs/world')
    expect(signalRMock.builder?.withAutomaticReconnect).toHaveBeenCalledOnce()
    expect(signalRMock.builder?.configureLogging).toHaveBeenCalledWith(2)

    await live.start()
    await live.stop()
    expect(signalRMock.connection.start).toHaveBeenCalledOnce()
    expect(signalRMock.connection.stop).toHaveBeenCalledOnce()
    expect(signalRMock.connection.stream).toHaveBeenCalledWith('StreamWorld')
    expect(signalRMock.subscription.dispose).toHaveBeenCalledOnce()
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

  it('strictly parses streamed frames before delivering them', async () => {
    const live = createWorldConnection()
    const frameHandler = vi.fn()
    const errorHandler = vi.fn()
    live.onWorldFrame(frameHandler)
    live.onStreamError(errorHandler)
    await live.start()

    const subscriber = signalRMock.getSubscriber()
    expect(subscriber).not.toBeNull()
    subscriber?.next({
      sequence: 1,
      sentAtUnixMilliseconds: 1000,
      revision: 2,
      status: { state: 'Running', worldMinute: 12, pendingEventCount: 1, worldSeed: '42', paused: false, operationalSpeed: 10 },
      citizens: [],
      structures: [],
    })
    expect(frameHandler).toHaveBeenCalledWith(expect.objectContaining({ sequence: 1, revision: 2, citizens: [], structures: [] }))

    subscriber?.next({ sequence: 0 })
    expect(errorHandler).toHaveBeenCalledOnce()
  })

  it('rejects stale subscriptions and duplicate frames, and restarts a completed stream on a connected hub', async () => {
    const live = createWorldConnection()
    const frameHandler = vi.fn()
    const errorHandler = vi.fn()
    live.onWorldFrame(frameHandler)
    live.onStreamError(errorHandler)
    await live.start()
    const old = signalRMock.getSubscriber()!
    const frame = { sequence: 1, sentAtUnixMilliseconds: 1000, revision: 20, status: { state: 'Running', worldMinute: 12, pendingEventCount: 1, worldSeed: '42', paused: false, operationalSpeed: 10 }, citizens: [], structures: [] }
    old.next(frame); old.next(frame)
    expect(frameHandler).toHaveBeenCalledTimes(1)
    old.complete()
    expect(errorHandler).toHaveBeenCalledOnce()
    signalRMock.connection.state = 'Connected'
    await live.start()
    expect(signalRMock.connection.start).toHaveBeenCalledOnce()
    old.next({ ...frame, sequence: 2 }); old.error(new Error('old stream'))
    expect(frameHandler).toHaveBeenCalledTimes(1)
    expect(errorHandler).toHaveBeenCalledOnce()
    signalRMock.getSubscriber()!.next({ ...frame, revision: 1 })
    expect(frameHandler).toHaveBeenCalledTimes(2)
  })
})
