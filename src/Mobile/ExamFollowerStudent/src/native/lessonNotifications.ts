import {NativeModules} from 'react-native';
import type {AlarmSummary, NativeLessonOccurrence} from '../types';

type LessonNotificationsNativeModule = {
  synchronize(
    items: NativeLessonOccurrence[],
    validUntilEpochMillis: number,
  ): Promise<AlarmSummary>;
  cancelAll(): Promise<void>;
  getSummary(): Promise<AlarmSummary>;
  openExactAlarmSettings(): Promise<boolean>;
};

const nativeModule = NativeModules
  .LessonNotifications as LessonNotificationsNativeModule;

export const lessonNotifications = {
  synchronize(
    items: NativeLessonOccurrence[],
    validUntilEpochMillis: number,
  ): Promise<AlarmSummary> {
    return nativeModule.synchronize(items, validUntilEpochMillis);
  },
  cancelAll(): Promise<void> {
    return nativeModule.cancelAll();
  },
  getSummary(): Promise<AlarmSummary> {
    return nativeModule.getSummary();
  },
  openExactAlarmSettings(): Promise<boolean> {
    return nativeModule.openExactAlarmSettings();
  },
};
