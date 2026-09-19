import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { parseCitizens, parseStatus, parseStructures, type Citizen, type Status, type Structure } from './api'

export type WorldChangedMessage = {
  revision: number
  worldMinute: number
  hostState: string
  persistenceState: string
}

export type WorldChangedHandler = (message: WorldChangedMessage) => void
export type ConnectionStateHandler = () => void
export type WorldFrame = {
  sequence: number
  sentAtUnixMilliseconds: number
  revision: number
  status: Status
  citizens: Citizen[]
  structures: Structure[]
}
export type WorldFrameHandler = (frame: WorldFrame) => void

function parseNonNegativeSafeInteger(value: unknown, label: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) throw new Error(`The server returned an invalid ${label}.`)
  return value
}

export function parseWorldFrame(value: unknown): WorldFrame {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) throw new Error('The server returned an invalid live world frame.')
  const frame = value as Record<string, unknown>
  const sequence = parseNonNegativeSafeInteger(frame.sequence, 'live frame sequence')
  if (sequence === 0) throw new Error('The server returned an invalid live frame sequence.')
  return {
    sequence,
    sentAtUnixMilliseconds: parseNonNegativeSafeInteger(frame.sentAtUnixMilliseconds, 'live frame timestamp'),
    revision: parseNonNegativeSafeInteger(frame.revision, 'live frame revision'),
    status: parseStatus(frame.status),
    citizens: parseCitizens(frame.citizens),
    structures: parseStructures(frame.structures),
  }
}

export interface WorldConnection {
  start: () => Promise<void>
  stop: () => Promise<void>
  onWorldChanged: (handler: WorldChangedHandler) => void
  onWorldFrame: (handler: WorldFrameHandler) => void
  onStreamError: (handler: ConnectionStateHandler) => void
  onReconnecting: (handler: ConnectionStateHandler) => void
  onReconnected: (handler: ConnectionStateHandler) => void
  onClose: (handler: ConnectionStateHandler) => void
}

export function createWorldConnection(): WorldConnection {
  const connection = new HubConnectionBuilder()
    .withUrl('/hubs/world')
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()

  let frameHandler: WorldFrameHandler | null = null
  let streamErrorHandler: ConnectionStateHandler | null = null
  let subscription: { dispose: () => void } | null = null
  let generation = 0
  let lastSequence = 0
  const stopStream = () => { generation += 1; subscription?.dispose(); subscription = null }
  const startStream = () => {
    stopStream()
    const currentGeneration = generation
    lastSequence = 0
    subscription = connection.stream('StreamWorld').subscribe({
      next: payload => {
        if (currentGeneration !== generation) return
        try {
          const frame = parseWorldFrame(payload)
          if (frame.sequence <= lastSequence) return
          lastSequence = frame.sequence
          frameHandler?.(frame)
        }
        catch { streamErrorHandler?.() }
      },
      error: () => { if (currentGeneration === generation) { subscription = null; streamErrorHandler?.() } },
      complete: () => { if (currentGeneration === generation) { subscription = null; streamErrorHandler?.() } },
    })
  }

  return {
    start: async () => { if (connection.state !== 'Connected') await connection.start(); startStream() },
    stop: async () => { stopStream(); await connection.stop() },
    onWorldChanged: handler => connection.on('worldChanged', payload => handler(payload as WorldChangedMessage)),
    onWorldFrame: handler => { frameHandler = handler },
    onStreamError: handler => { streamErrorHandler = handler },
    onReconnecting: handler => connection.onreconnecting(() => { stopStream(); handler() }),
    onReconnected: handler => connection.onreconnected(() => { handler(); startStream() }),
    onClose: handler => connection.onclose(() => { stopStream(); handler() }),
  }
}
