/**
 * signalr.ts — thin builder around `@microsoft/signalr`'s
 * `HubConnectionBuilder`. Sprint-7 plan U7.
 *
 * `buildConnection` is the single seam between the SDK and the hook layer.
 * The hook in `useSignalR.ts` calls it once per `connect()`, wires the
 * lifecycle callbacks, registers per-event handlers for the known event
 * names, and returns the underlying `HubConnection` so the caller can
 * `start()` / `stop()` it.
 *
 * Why a wrapper at all:
 *   - Keeps `useSignalR.ts` free of `@microsoft/signalr` imports beyond
 *     the type used in module state (cleaner test seam).
 *   - Centralises connection options (`withAutomaticReconnect`,
 *     `configureLogging(LogLevel.Warning)`) so the policy lives in one
 *     place and downstream tests can ignore it.
 *
 * Auth: `accessTokenFactory` is called by the SignalR client on every
 * negotiate / reconnect; passing a closure that reads `useAuth.getState()`
 * keeps the latest JWT in scope without having to rebuild the connection.
 */

import {
  HubConnectionBuilder,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr';

export type SignalRState =
  | 'idle'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'disconnected';

export interface BuildConnectionOptions {
  url: string;
  accessTokenFactory: () => string | null | Promise<string | null>;
  onStateChange: (state: SignalRState) => void;
  onEvent: (eventName: string, payload: unknown) => void;
  /** Hub event names to wire to `onEvent`. Defaults to an empty list. */
  eventNames?: string[];
}

export function buildConnection(opts: BuildConnectionOptions): HubConnection {
  const { url, accessTokenFactory, onStateChange, onEvent, eventNames = [] } = opts;

  const connection = new HubConnectionBuilder()
    .withUrl(url, {
      // `@microsoft/signalr` accepts `string | Promise<string>` here.
      // Returning null from our factory is mapped to an empty string so
      // the SDK falls back to its anonymous-negotiate path; the hook only
      // calls `start()` when the JWT is non-null anyway.
      accessTokenFactory: () => {
        const token = accessTokenFactory();
        if (token === null) return '';
        if (typeof token === 'string') return token;
        return token.then((t) => t ?? '');
      },
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();

  connection.onreconnecting(() => onStateChange('reconnecting'));
  connection.onreconnected(() => onStateChange('connected'));
  connection.onclose(() => onStateChange('disconnected'));

  for (const eventName of eventNames) {
    connection.on(eventName, (payload: unknown) => onEvent(eventName, payload));
  }

  return connection;
}
