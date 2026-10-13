export type StudentUser = {
  id: string;
  email: string;
  displayName: string;
  role: 'Student';
};

export type StoredSession = {
  accessToken: string;
  expiresAtUtc: string;
  user: StudentUser;
};

export type LessonOccurrence = {
  occurrenceId: string;
  scheduleId: string;
  title: string;
  startsAtUtc: string;
  endsAtUtc: string;
  reminderAtUtc: string;
  lockRequired: boolean;
  source: string;
};

export type LessonFeed = {
  generatedAtUtc: string;
  validUntilUtc: string;
  items: LessonOccurrence[];
  rejectedCount: number;
};

export type NativeLessonOccurrence = {
  occurrenceId: string;
  title: string;
  startsAtEpochMillis: number;
  endsAtEpochMillis: number;
};

export type AlarmSummary = {
  scheduledCount: number;
  addedCount: number;
  changedCount: number;
  removedCount: number;
  invalidCount: number;
  exactAlarmAvailable: boolean;
  fallbackUsed: boolean;
  notificationPermissionGranted: boolean;
};
