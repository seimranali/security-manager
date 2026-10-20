import { useEffect, useState } from 'react';
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import type { Alert } from '../lib/types';
import { getValidAccessToken } from '../lib/auth';

export type HubStatus = 'connecting' | 'live' | 'offline';

/** Subscribes to real-time alert pushes and keeps cached queries fresh. */
export function useAlertHub(): HubStatus {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<HubStatus>('connecting');

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      // Called on every (re)connect, so reconnections always use a fresh access token.
      .withUrl('/hubs/alerts', { accessTokenFactory: getValidAccessToken })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    const refresh = () => {
      queryClient.invalidateQueries({ queryKey: ['alerts'] });
      queryClient.invalidateQueries({ queryKey: ['detections'] });
      queryClient.invalidateQueries({ queryKey: ['scan-jobs'] });
    };

    connection.on('alertCreated', (alert: Alert) => {
      refresh();
      const notify = alert.severity === 'Critical' || alert.severity === 'High' ? toast.error : toast.warning;
      notify(alert.title, { description: `${alert.severity} · ${alert.source}` });
    });
    connection.on('alertUpdated', () => queryClient.invalidateQueries({ queryKey: ['alerts'] }));
    connection.onreconnecting(() => setStatus('connecting'));
    connection.onreconnected(() => setStatus('live'));
    connection.onclose(() => setStatus('offline'));

    let disposed = false;
    const start = async () => {
      try {
        await connection.start();
        if (!disposed) setStatus('live');
      } catch {
        if (!disposed) {
          setStatus('offline');
          setTimeout(() => !disposed && connection.state === HubConnectionState.Disconnected && start(), 10000);
        }
      }
    };
    start();

    return () => {
      disposed = true;
      connection.stop();
    };
  }, [queryClient]);

  return status;
}
