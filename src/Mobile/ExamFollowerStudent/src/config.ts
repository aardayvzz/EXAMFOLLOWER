import {NativeModules} from 'react-native';

type AppConfigNativeModule = {
  apiBaseUrl?: string;
};

const nativeConfig = NativeModules.AppConfig as AppConfigNativeModule | undefined;
const configuredUrl = nativeConfig?.apiBaseUrl?.trim().replace(/\/$/, '') ?? '';

export function getApiBaseUrl(): string {
  if (!configuredUrl || !/^https?:\/\//i.test(configuredUrl)) {
    throw new Error('API adresi bu build için yapılandırılmamış.');
  }
  return configuredUrl;
}
