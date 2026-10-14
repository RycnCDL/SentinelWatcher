// Sign-in settings served by the API at /config.json. Loading them at runtime means
// one client build works for every tenant: nothing tenant-specific is baked in.
export interface RuntimeConfig {
  clientId: string;
  tenantId: string;
  authority: string;
  apiScope: string;
}

export async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  const response = await fetch('/config.json', { cache: 'no-store' });
  const body: unknown = await response.json().catch(() => null);

  if (!response.ok) {
    const message = (body as { message?: string } | null)?.message;
    throw new Error(message ?? `Could not load /config.json (HTTP ${response.status}).`);
  }

  const config = body as Partial<RuntimeConfig> | null;
  if (!config?.clientId || !config.tenantId || !config.authority || !config.apiScope) {
    throw new Error('The /config.json response is incomplete.');
  }
  return config as RuntimeConfig;
}
