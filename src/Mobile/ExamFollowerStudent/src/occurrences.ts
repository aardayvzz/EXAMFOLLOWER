import type {
  LessonFeed,
  LessonOccurrence,
  NativeLessonOccurrence,
} from './types';

type UnknownRecord = Record<string, unknown>;

function isRecord(value: unknown): value is UnknownRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function nonEmptyString(value: unknown, maxLength: number): value is string {
  return (
    typeof value === 'string' &&
    value.trim().length > 0 &&
    value.length <= maxLength
  );
}

export function parseUtcInstant(value: unknown): number | null {
  if (
    typeof value !== 'string' ||
    !/(?:Z|[+-]\d{2}:\d{2})$/i.test(value)
  ) {
    return null;
  }
  const timestamp = Date.parse(value);
  return Number.isFinite(timestamp) ? timestamp : null;
}

function normalizeOccurrence(
  value: unknown,
  now: number,
): LessonOccurrence | null {
  if (!isRecord(value)) {
    return null;
  }

  const startsAt = parseUtcInstant(value.startsAtUtc);
  const endsAt = parseUtcInstant(value.endsAtUtc);
  const reminderAt = parseUtcInstant(value.reminderAtUtc);
  if (
    !nonEmptyString(value.occurrenceId, 256) ||
    !nonEmptyString(value.scheduleId, 64) ||
    !nonEmptyString(value.title, 200) ||
    !nonEmptyString(value.source, 64) ||
    startsAt === null ||
    endsAt === null ||
    reminderAt === null ||
    startsAt <= now ||
    endsAt <= startsAt ||
    reminderAt > startsAt ||
    typeof value.lockRequired !== 'boolean'
  ) {
    return null;
  }

  return {
    occurrenceId: value.occurrenceId.trim(),
    scheduleId: value.scheduleId,
    title: value.title.trim(),
    startsAtUtc: new Date(startsAt).toISOString(),
    endsAtUtc: new Date(endsAt).toISOString(),
    reminderAtUtc: new Date(reminderAt).toISOString(),
    lockRequired: value.lockRequired,
    source: value.source.trim(),
  };
}

export function normalizeLessonFeed(value: unknown, now = Date.now()): LessonFeed {
  if (!isRecord(value) || !Array.isArray(value.items)) {
    throw new Error('Ders listesi sunucudan beklenmeyen biçimde geldi.');
  }

  const generatedAt = parseUtcInstant(value.generatedAtUtc);
  const validUntil = parseUtcInstant(value.validUntilUtc);
  if (generatedAt === null || validUntil === null || validUntil < generatedAt) {
    throw new Error('Ders listesi zaman bilgileri geçersiz.');
  }

  const byId = new Map<string, LessonOccurrence>();
  let rejectedCount = 0;
  for (const item of value.items) {
    const occurrence = normalizeOccurrence(item, now);
    if (!occurrence || byId.has(occurrence.occurrenceId)) {
      rejectedCount += 1;
      continue;
    }
    byId.set(occurrence.occurrenceId, occurrence);
  }

  return {
    generatedAtUtc: new Date(generatedAt).toISOString(),
    validUntilUtc: new Date(validUntil).toISOString(),
    items: [...byId.values()].sort(
      (left, right) =>
        Date.parse(left.startsAtUtc) - Date.parse(right.startsAtUtc),
    ),
    rejectedCount,
  };
}

export function toNativeOccurrences(
  items: LessonOccurrence[],
): NativeLessonOccurrence[] {
  return items.map(item => ({
    occurrenceId: item.occurrenceId,
    title: item.title,
    startsAtEpochMillis: Date.parse(item.startsAtUtc),
    endsAtEpochMillis: Date.parse(item.endsAtUtc),
  }));
}

export type SnapshotDiff = {
  added: LessonOccurrence[];
  changed: LessonOccurrence[];
  removed: LessonOccurrence[];
  unchanged: LessonOccurrence[];
};

export function compareSnapshots(
  previous: LessonOccurrence[],
  next: LessonOccurrence[],
): SnapshotDiff {
  const previousById = new Map(previous.map(item => [item.occurrenceId, item]));
  const nextById = new Map(next.map(item => [item.occurrenceId, item]));
  const added: LessonOccurrence[] = [];
  const changed: LessonOccurrence[] = [];
  const unchanged: LessonOccurrence[] = [];

  for (const item of next) {
    const old = previousById.get(item.occurrenceId);
    if (!old) {
      added.push(item);
    } else if (
      old.startsAtUtc !== item.startsAtUtc ||
      old.endsAtUtc !== item.endsAtUtc ||
      old.title !== item.title
    ) {
      changed.push(item);
    } else {
      unchanged.push(item);
    }
  }

  return {
    added,
    changed,
    unchanged,
    removed: previous.filter(item => !nextById.has(item.occurrenceId)),
  };
}
