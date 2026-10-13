import * as Keychain from 'react-native-keychain';
import type {StoredSession} from './types';

const SERVICE = 'com.examfollower.student.session';
const USERNAME = 'student-session';

export async function saveSession(session: StoredSession): Promise<void> {
  await Keychain.setGenericPassword(USERNAME, JSON.stringify(session), {
    service: SERVICE,
    accessible: Keychain.ACCESSIBLE.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
    securityLevel: Keychain.SECURITY_LEVEL.SECURE_SOFTWARE,
  });
}

export async function loadSession(now = Date.now()): Promise<StoredSession | null> {
  const credentials = await Keychain.getGenericPassword({service: SERVICE});
  if (!credentials) {
    return null;
  }

  try {
    const candidate = JSON.parse(credentials.password) as StoredSession;
    const expiresAt = Date.parse(candidate.expiresAtUtc);
    if (
      !candidate.accessToken ||
      candidate.user?.role !== 'Student' ||
      !Number.isFinite(expiresAt) ||
      expiresAt <= now
    ) {
      await clearSession();
      return null;
    }
    return candidate;
  } catch {
    await clearSession();
    return null;
  }
}

export async function clearSession(): Promise<void> {
  await Keychain.resetGenericPassword({service: SERVICE});
}
