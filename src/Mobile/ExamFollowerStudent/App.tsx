import React, {useCallback, useEffect, useMemo, useState} from 'react';
import {
  ActivityIndicator,
  Alert,
  FlatList,
  PermissionsAndroid,
  Platform,
  Pressable,
  StatusBar,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import {SafeAreaProvider, SafeAreaView} from 'react-native-safe-area-context';
import {ApiClient, ApiError} from './src/api';
import {lessonNotifications} from './src/native/lessonNotifications';
import {toNativeOccurrences} from './src/occurrences';
import {clearSession, loadSession, saveSession} from './src/session';
import type {AlarmSummary, LessonOccurrence, StoredSession} from './src/types';

type ScreenState = 'booting' | 'loggedOut' | 'ready';

function App() {
  const [screen, setScreen] = useState<ScreenState>('booting');
  const [session, setSession] = useState<StoredSession | null>(null);
  const [lessons, setLessons] = useState<LessonOccurrence[]>([]);
  const [summary, setSummary] = useState<AlarmSummary | null>(null);
  const [status, setStatus] = useState('Oturum kontrol ediliyor…');
  const [busy, setBusy] = useState(false);

  const signOutLocally = useCallback(async () => {
    await clearSession();
    await lessonNotifications.cancelAll();
    setSession(null);
    setLessons([]);
    setSummary(null);
    setScreen('loggedOut');
  }, []);

  const api = useMemo(
    () =>
      new ApiClient({
        getToken: () => session?.accessToken ?? null,
        onUnauthorized: signOutLocally,
      }),
    [session?.accessToken, signOutLocally],
  );

  const synchronize = useCallback(async () => {
    if (!session) {
      return;
    }
    setBusy(true);
    setStatus('Dersler sunucudan alınıyor…');
    try {
      setSummary(await lessonNotifications.getSummary());
      const feed = await api.upcomingLessons();
      const nextSummary = await lessonNotifications.synchronize(
        toNativeOccurrences(feed.items),
        Date.parse(feed.validUntilUtc),
      );
      setLessons(feed.items);
      setSummary(nextSummary);
      const rejected = feed.rejectedCount
        ? ` ${feed.rejectedCount} geçersiz kayıt atlandı.`
        : '';
      setStatus(
        `${nextSummary.scheduledCount} ders bildirimi planlandı.${rejected}`,
      );
    } catch (error) {
      setStatus(
        error instanceof Error
          ? error.message
          : 'Dersler yenilenemedi. Mevcut alarmlar korundu.',
      );
    } finally {
      setBusy(false);
    }
  }, [api, session]);

  useEffect(() => {
    let active = true;
    runAsync((async () => {
      try {
        const restored = await loadSession();
        if (!active) {
          return;
        }
        if (!restored) {
          setScreen('loggedOut');
          return;
        }
        setSession(restored);
        setScreen('ready');
        setStatus('Güvenli oturum bulundu. Dersler yenileniyor…');
      } catch {
        if (active) {
          setStatus('Güvenli oturum okunamadı.');
          setScreen('loggedOut');
        }
      }
    })());
    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    if (screen === 'ready' && session) {
      runAsync(synchronize());
    }
  }, [screen, session, synchronize]);

  const handleLogin = async (email: string, password: string) => {
    setBusy(true);
    setStatus('Giriş yapılıyor…');
    try {
      const nextSession = await new ApiClient().login(email, password);
      await saveSession(nextSession);
      setSession(nextSession);
      setScreen('ready');
      setStatus('Giriş başarılı. Dersler yenileniyor…');
    } catch (error) {
      setStatus(
        error instanceof ApiError
          ? error.message
          : 'Giriş tamamlanamadı. Lütfen tekrar deneyin.',
      );
    } finally {
      setBusy(false);
    }
  };

  const requestNotificationPermission = () => {
    Alert.alert(
      'Bildirim izni',
      'Dersiniz başladığında tablette uyarı gösterebilmek için bildirim izni gerekir.',
      [
        {text: 'Vazgeç', style: 'cancel'},
        {
          text: 'İzin iste',
          onPress: () => {
            runAsync((async () => {
              if (Platform.OS === 'android' && Platform.Version >= 33) {
                await PermissionsAndroid.request(
                  PermissionsAndroid.PERMISSIONS.POST_NOTIFICATIONS,
                );
              }
              setSummary(await lessonNotifications.getSummary());
            })());
          },
        },
      ],
    );
  };

  const openExactAlarmSettings = () => {
    Alert.alert(
      'Kesin alarm erişimi',
      'Kesin alarm erişimi olmadan Android bildirimi yaklaşık zamanda gösterebilir. Ayarlardan bu uygulamaya kesin alarm izni verebilirsiniz.',
      [
        {text: 'Vazgeç', style: 'cancel'},
        {
          text: 'Ayarları aç',
          onPress: () => runAsync(lessonNotifications.openExactAlarmSettings()),
        },
      ],
    );
  };

  if (screen === 'booting') {
    return <LoadingScreen status={status} />;
  }

  return (
    <SafeAreaProvider>
      <StatusBar barStyle="dark-content" />
      {screen === 'loggedOut' ? (
        <LoginScreen busy={busy} status={status} onLogin={handleLogin} />
      ) : (
        <SafeAreaView style={styles.page} edges={['top', 'left', 'right']}>
          <View style={styles.header}>
            <View>
              <Text style={styles.heading}>Yaklaşan Dersler</Text>
              <Text style={styles.subtle}>{session?.user.displayName}</Text>
            </View>
            <Pressable style={styles.linkButton} onPress={() => runAsync(signOutLocally())}>
              <Text style={styles.linkText}>Çıkış yap</Text>
            </Pressable>
          </View>

          <View style={styles.statusCard}>
            <Text style={styles.statusTitle}>Bildirim durumu</Text>
            <Text style={styles.statusText}>{status}</Text>
            {summary?.fallbackUsed ? (
              <Text style={styles.warning}>
                Kesin alarm erişimi yok; güvenli yaklaşık zaman fallback'i kullanılıyor.
              </Text>
            ) : null}
            {summary && !summary.notificationPermissionGranted ? (
              <Pressable style={styles.secondaryButton} onPress={requestNotificationPermission}>
                <Text style={styles.secondaryButtonText}>Bildirim iznini açıkla</Text>
              </Pressable>
            ) : null}
            {summary && !summary.exactAlarmAvailable ? (
              <Pressable style={styles.secondaryButton} onPress={openExactAlarmSettings}>
                <Text style={styles.secondaryButtonText}>Kesin alarm seçeneğini açıkla</Text>
              </Pressable>
            ) : null}
          </View>

          <Pressable
            disabled={busy}
            style={[styles.primaryButton, busy && styles.disabled]}
            onPress={() => runAsync(synchronize())}>
            <Text style={styles.primaryButtonText}>
              {busy ? 'Yenileniyor…' : 'Dersleri Yenile'}
            </Text>
          </Pressable>

          <FlatList
            data={lessons}
            keyExtractor={item => item.occurrenceId}
            contentContainerStyle={styles.list}
            ListEmptyComponent={
              <Text style={styles.empty}>Yaklaşan etkin ders bulunmuyor.</Text>
            }
            renderItem={({item}) => (
              <View style={styles.lessonCard}>
                <Text style={styles.lessonTitle}>{item.title}</Text>
                <Text style={styles.lessonTime}>{formatLocalTime(item.startsAtUtc)}</Text>
              </View>
            )}
          />
        </SafeAreaView>
      )}
    </SafeAreaProvider>
  );
}

function LoadingScreen({status}: {status: string}) {
  return (
    <SafeAreaProvider>
      <SafeAreaView style={[styles.page, styles.centered]}>
        <ActivityIndicator size="large" color="#275dad" />
        <Text style={styles.statusText}>{status}</Text>
      </SafeAreaView>
    </SafeAreaProvider>
  );
}

function LoginScreen({
  busy,
  status,
  onLogin,
}: {
  busy: boolean;
  status: string;
  onLogin: (email: string, password: string) => Promise<void>;
}) {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  return (
    <SafeAreaView style={[styles.page, styles.loginPage]}>
      <Text style={styles.heading}>Sınav Asistanı</Text>
      <Text style={styles.subtle}>Öğrenci hesabınızla giriş yapın.</Text>
      <TextInput
        autoCapitalize="none"
        autoComplete="email"
        keyboardType="email-address"
        placeholder="E-posta"
        style={styles.input}
        value={email}
        onChangeText={setEmail}
      />
      <TextInput
        autoCapitalize="none"
        autoComplete="current-password"
        placeholder="Parola"
        secureTextEntry
        style={styles.input}
        value={password}
        onChangeText={setPassword}
      />
      <Pressable
        disabled={busy || !email.trim() || !password}
        style={[styles.primaryButton, (busy || !email.trim() || !password) && styles.disabled]}
        onPress={() => runAsync(onLogin(email, password))}>
        <Text style={styles.primaryButtonText}>{busy ? 'Giriş yapılıyor…' : 'Giriş Yap'}</Text>
      </Pressable>
      <Text style={styles.statusText}>{status}</Text>
    </SafeAreaView>
  );
}

function formatLocalTime(value: string): string {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'full',
    timeStyle: 'short',
  }).format(new Date(value));
}

function runAsync(promise: Promise<unknown>): void {
  promise.catch(() => undefined);
}

const styles = StyleSheet.create({
  page: {flex: 1, backgroundColor: '#f5f7fb', padding: 24},
  centered: {alignItems: 'center', justifyContent: 'center', gap: 16},
  loginPage: {justifyContent: 'center', gap: 14},
  header: {flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between'},
  heading: {fontSize: 28, fontWeight: '700', color: '#15213a'},
  subtle: {fontSize: 15, color: '#5b6579', marginTop: 4},
  input: {backgroundColor: '#fff', borderColor: '#cfd6e4', borderWidth: 1, borderRadius: 10, paddingHorizontal: 14, paddingVertical: 12, fontSize: 17, color: '#15213a'},
  primaryButton: {backgroundColor: '#275dad', borderRadius: 10, padding: 14, alignItems: 'center', marginTop: 14},
  primaryButtonText: {color: '#fff', fontSize: 17, fontWeight: '700'},
  secondaryButton: {borderColor: '#275dad', borderWidth: 1, borderRadius: 8, padding: 10, marginTop: 10},
  secondaryButtonText: {color: '#275dad', textAlign: 'center', fontWeight: '600'},
  disabled: {opacity: 0.5},
  statusCard: {backgroundColor: '#fff', borderRadius: 12, padding: 16, marginTop: 18},
  statusTitle: {fontSize: 17, fontWeight: '700', color: '#15213a'},
  statusText: {fontSize: 15, color: '#465168', marginTop: 8},
  warning: {fontSize: 14, color: '#8a4c00', marginTop: 8},
  list: {paddingVertical: 18, gap: 10},
  lessonCard: {backgroundColor: '#fff', borderRadius: 10, padding: 16},
  lessonTitle: {fontSize: 18, fontWeight: '700', color: '#15213a'},
  lessonTime: {fontSize: 15, color: '#465168', marginTop: 6},
  empty: {textAlign: 'center', color: '#5b6579', marginTop: 32},
  linkButton: {padding: 8},
  linkText: {color: '#275dad', fontWeight: '600'},
});

export default App;
