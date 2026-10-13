import {
  compareSnapshots,
  normalizeLessonFeed,
  parseUtcInstant,
} from '../src/occurrences';
import type {LessonOccurrence} from '../src/types';

const NOW = Date.parse('2026-10-12T12:00:00Z');

function occurrence(
  overrides: Partial<Record<keyof LessonOccurrence, unknown>> = {},
): Record<string, unknown> {
  return {
    occurrenceId: 'schedule-1:2026-10-12',
    scheduleId: '00000000-0000-0000-0000-000000000001',
    title: 'Matematik',
    startsAtUtc: '2026-10-12T15:00:00Z',
    endsAtUtc: '2026-10-12T16:00:00Z',
    reminderAtUtc: '2026-10-12T15:00:00Z',
    lockRequired: true,
    source: 'Kurspro',
    ...overrides,
  };
}

function feed(items: unknown[]): Record<string, unknown> {
  return {
    generatedAtUtc: '2026-10-12T12:00:00Z',
    validUntilUtc: '2026-10-20T12:00:00Z',
    items,
  };
}

describe('lesson occurrence normalization', () => {
  test('normalizes the real API envelope', () => {
    const normalized = normalizeLessonFeed(feed([occurrence()]), NOW);
    expect(normalized.items).toHaveLength(1);
    expect(normalized.items[0].startsAtUtc).toBe('2026-10-12T15:00:00.000Z');
    expect(normalized.rejectedCount).toBe(0);
  });

  test('parses UTC offsets as absolute instants', () => {
    expect(parseUtcInstant('2026-10-12T18:00:00+03:00')).toBe(
      Date.parse('2026-10-12T15:00:00Z'),
    );
    expect(parseUtcInstant('2026-10-12T18:00:00')).toBeNull();
  });

  test('filters past occurrences', () => {
    const normalized = normalizeLessonFeed(
      feed([occurrence({startsAtUtc: '2026-10-12T11:00:00Z'})]),
      NOW,
    );
    expect(normalized.items).toEqual([]);
    expect(normalized.rejectedCount).toBe(1);
  });

  test('rejects invalid records and duplicate occurrence identifiers', () => {
    const normalized = normalizeLessonFeed(
      feed([
        occurrence(),
        occurrence(),
        occurrence({occurrenceId: ''}),
        occurrence({endsAtUtc: 'not-a-date'}),
      ]),
      NOW,
    );
    expect(normalized.items).toHaveLength(1);
    expect(normalized.rejectedCount).toBe(3);
  });
});

describe('snapshot comparison', () => {
  test('finds added, changed, removed and unchanged lessons', () => {
    const oldA = normalizeLessonFeed(feed([occurrence()]), NOW).items[0];
    const oldB = {...oldA, occurrenceId: 'removed'};
    const changedA = {...oldA, startsAtUtc: '2026-10-12T15:30:00.000Z'};
    const added = {...oldA, occurrenceId: 'added'};

    const diff = compareSnapshots([oldA, oldB], [changedA, added]);
    expect(diff.changed.map(item => item.occurrenceId)).toEqual([
      oldA.occurrenceId,
    ]);
    expect(diff.added.map(item => item.occurrenceId)).toEqual(['added']);
    expect(diff.removed.map(item => item.occurrenceId)).toEqual(['removed']);
    expect(diff.unchanged).toEqual([]);
  });

  test('does not add the same occurrence twice', () => {
    const item = normalizeLessonFeed(feed([occurrence()]), NOW).items[0];
    const diff = compareSnapshots([item], [item]);
    expect(diff.added).toEqual([]);
    expect(diff.changed).toEqual([]);
    expect(diff.unchanged).toEqual([item]);
  });
});
