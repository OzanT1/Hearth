import { useEffect, useRef, useState } from 'react';
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import type { HubConnection } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';

interface Typing { conversationId: string; userId: string; name: string; until: number }
export function useLive(userId?: string) {
  const cache = useQueryClient(); const [connection, setConnection] = useState<HubConnection>();
  const lastTyping = useRef(0);
  const [status, setStatus] = useState('connecting'); const [typing, setTyping] = useState<Typing[]>([]);
  useEffect(() => {
    if (!userId) return;
    let stopped = false; let retryTimer: ReturnType<typeof setTimeout> | undefined;
    const hub = new HubConnectionBuilder().withUrl('/hubs/chat').withAutomaticReconnect([0, 1000, 3000, 5000, 10000]).configureLogging(LogLevel.Error).build();
    setConnection(hub);
    const refresh = () => { void cache.invalidateQueries(); };
    hub.on('changed', (event: { conversationId?: string; kind: string }) => {
      if (event.conversationId) void cache.invalidateQueries({ queryKey: ['messages', event.conversationId] });
      void cache.invalidateQueries({ queryKey: ['channels'] }); void cache.invalidateQueries({ queryKey: ['dms'] });
      if (!event.conversationId && event.kind !== 'read') refresh();
    });
    hub.on('typing', (event: Omit<Typing, 'until'>) => setTyping(previous => [...previous.filter(x => x.userId !== event.userId), { ...event, until: Date.now() + 4000 }]));
    hub.on('presence', () => { void cache.invalidateQueries({ queryKey: ['members'] }); });
    hub.onreconnecting(() => setStatus('reconnecting'));
    hub.onreconnected(() => { setStatus('connected'); refresh(); });
    async function start() {
      if (stopped) return;
      try { await hub.start(); if (!stopped) { setStatus('connected'); refresh(); } }
      catch { if (!stopped) { setStatus('offline'); retryTimer = setTimeout(() => void start(), 5000); } }
    }
    hub.onclose(() => { if (!stopped) { setStatus('offline'); void cache.invalidateQueries({ queryKey: ['me'] }); retryTimer = setTimeout(() => void start(), 5000); } });
    void start();
    const heartbeat = setInterval(() => { if (hub.state === HubConnectionState.Connected) void hub.invoke('Heartbeat').catch(() => {}); }, 25000);
    const cleanup = setInterval(() => setTyping(previous => previous.filter(x => x.until > Date.now())), 1000);
    return () => { stopped = true; clearTimeout(retryTimer); clearInterval(heartbeat); clearInterval(cleanup); void hub.stop(); };
  }, [userId, cache]);
  return { status, typing, type: (id: string) => {
    const now = Date.now();
    if (now - lastTyping.current < 1000 || connection?.state !== HubConnectionState.Connected) return;
    lastTyping.current = now;
    void connection.invoke('Typing', id).catch(() => {});
  } };
}
