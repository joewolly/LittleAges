import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'

export type WorldChangedMessage = {
  revision: number
  worldMinute: number
  hostState: string
  persistenceState: string
}

export type WorldChangedHandler = (message: WorldChangedMessage) => void
export type ConnectionStateHandler = () => void

export interface WorldConnection {
  start: () => Promise<void>
  stop: () => Promise<void>
  onWorldChanged: (handler: WorldChangedHandler) => void
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

  return {
    start: () => connection.start(),
    stop: () => connection.stop(),
    onWorldChanged: handler => connection.on('worldChanged', payload => handler(payload as WorldChangedMessage)),
    onReconnecting: handler => connection.onreconnecting(() => handler()),
    onReconnected: handler => connection.onreconnected(() => handler()),
    onClose: handler => connection.onclose(() => handler()),
  }
}
