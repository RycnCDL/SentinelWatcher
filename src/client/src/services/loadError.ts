import { isAxiosError } from 'axios';
import type { LoadError } from '../types';

export const LOAD_ERROR_TITLE = 'Could not load data, see server log';

// Turns a failed API call into a LoadError. The API answers a failed section with
// 502 { error: { code, message } }; anything else (network, 500, still loading) is mapped here.
export function loadErrorOf(err: unknown): LoadError {
  if (isAxiosError(err)) {
    const body = err.response?.data as { error?: Partial<LoadError>; message?: string } | undefined;
    if (body?.error?.code && body.error.message) {
      return { code: body.error.code, message: body.error.message };
    }
    if (err.response?.status === 503) {
      return { code: 'Loading', message: body?.message ?? 'Data is still loading. Try again shortly.' };
    }
    const status = err.response?.status;
    return {
      code: 'RequestFailed',
      message: status
        ? `The API answered with HTTP ${status}. See the server log.`
        : 'The API could not be reached. See the server log.',
    };
  }
  return { code: 'RequestFailed', message: 'The request failed. See the server log.' };
}
