import {getApiBaseUrl} from './config';
import {normalizeLessonFeed} from './occurrences';
import type {LessonFeed, StoredSession, StudentUser} from './types';

type FetchLike = typeof fetch;

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status?: number,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

type ApiClientOptions = {
  getToken?: () => string | null;
  onUnauthorized?: () => void | Promise<void>;
  fetchImpl?: FetchLike;
  baseUrl?: string;
  timeoutMs?: number;
};

export class ApiClient {
  private readonly fetchImpl: FetchLike;
  private readonly timeoutMs: number;

  constructor(private readonly options: ApiClientOptions = {}) {
    this.fetchImpl = options.fetchImpl ?? fetch;
    this.timeoutMs = options.timeoutMs ?? 15_000;
  }

  async login(email: string, password: string): Promise<StoredSession> {
    const payload = await this.request<unknown>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({email: email.trim(), password}),
    });
    return normalizeAuthResponse(payload);
  }

  async upcomingLessons(now = Date.now()): Promise<LessonFeed> {
    const payload = await this.request<unknown>(
      '/api/lesson-notifications/upcoming?days=30',
      {method: 'GET'},
      true,
    );
    return normalizeLessonFeed(payload, now);
  }

  private async request<T>(
    path: string,
    init: RequestInit,
    authenticated = false,
  ): Promise<T> {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.timeoutMs);
    const token = authenticated ? this.options.getToken?.() : null;
    if (authenticated && !token) {
      throw new ApiError('Oturum bulunamadı.', 401);
    }

    try {
      const response = await this.fetchImpl(
        `${this.options.baseUrl ?? getApiBaseUrl()}${path}`,
        {
          ...init,
          signal: controller.signal,
          headers: {
            Accept: 'application/json',
            'Content-Type': 'application/json',
            ...(token ? {Authorization: `Bearer ${token}`} : {}),
            ...init.headers,
          },
        },
      );

      if (response.status === 401) {
        if (authenticated) {
          await this.options.onUnauthorized?.();
          throw new ApiError(
            'Oturumunuzun süresi doldu. Lütfen tekrar giriş yapın.',
            401,
          );
        }
        throw new ApiError('E-posta veya parola geçersiz.', 401);
      }
      if (!response.ok) {
        throw new ApiError(userMessageForStatus(response.status), response.status);
      }
      return (await response.json()) as T;
    } catch (error) {
      if (error instanceof ApiError) {
        throw error;
      }
      if (error instanceof Error && error.name === 'AbortError') {
        throw new ApiError('Sunucu yanıt vermedi. Lütfen tekrar deneyin.');
      }
      throw new ApiError('Sunucuya ulaşılamadı. Bağlantınızı kontrol edin.');
    } finally {
      clearTimeout(timeout);
    }
  }
}

function normalizeAuthResponse(value: unknown): StoredSession {
  if (typeof value !== 'object' || value === null) {
    throw new ApiError('Giriş yanıtı geçersiz.');
  }
  const record = value as Record<string, unknown>;
  const user = record.user as Record<string, unknown> | undefined;
  const expiresAt =
    typeof record.expiresAtUtc === 'string'
      ? Date.parse(record.expiresAtUtc)
      : Number.NaN;
  if (
    typeof record.accessToken !== 'string' ||
    !record.accessToken ||
    record.tokenType !== 'Bearer' ||
    !Number.isFinite(expiresAt) ||
    !user ||
    typeof user.id !== 'string' ||
    typeof user.email !== 'string' ||
    typeof user.displayName !== 'string'
  ) {
    throw new ApiError('Giriş yanıtı geçersiz.');
  }
  if (user.role !== 'Student') {
    throw new ApiError('Bu uygulama yalnızca öğrenci hesapları içindir.', 403);
  }
  return {
    accessToken: record.accessToken,
    expiresAtUtc: new Date(expiresAt).toISOString(),
    user: user as StudentUser,
  };
}

function userMessageForStatus(status: number): string {
  if (status === 400) {
    return 'Gönderilen bilgiler geçersiz.';
  }
  if (status === 403) {
    return 'Bu işlem için yetkiniz yok.';
  }
  if (status === 429) {
    return 'Çok fazla deneme yapıldı. Lütfen biraz bekleyin.';
  }
  return 'Sunucu işlemi tamamlayamadı. Lütfen tekrar deneyin.';
}
