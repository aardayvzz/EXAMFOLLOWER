package com.examfollower.student.notifications

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Test

class LessonAlarmModelsTest {
  @Test
  fun stableAlarmIdIsDeterministicAndPreservesExistingAllocation() {
    val first = StableAlarmId.assign(listOf("occurrence-a"), emptyList())
      .getValue("occurrence-a")
    val previous = listOf(record("occurrence-a", requestCode = first))
    val second = StableAlarmId.assign(
      listOf("occurrence-a", "occurrence-b"),
      previous,
    )
    assertEquals(first, second.getValue("occurrence-a"))
    assertNotEquals(first, second.getValue("occurrence-b"))
  }

  @Test
  fun unchangedOccurrenceDoesNotCreateDuplicateAlarm() {
    val item = record("same")
    val plan = AlarmPlan.create(listOf(item), listOf(item))
    assertEquals(0, plan.added.size)
    assertEquals(0, plan.changed.size)
    assertEquals(1, plan.unchanged.size)
  }

  @Test
  fun changedStartTimeIsRescheduled() {
    val old = record("moving", start = 2_000)
    val changed = old.copy(startsAtEpochMillis = 3_000)
    val plan = AlarmPlan.create(listOf(old), listOf(changed))
    assertEquals(listOf(changed), plan.changed)
  }

  @Test
  fun removedOccurrenceIsCancelled() {
    val removed = record("removed")
    val plan = AlarmPlan.create(listOf(removed), emptyList())
    assertEquals(listOf(removed), plan.removed)
  }

  @Test
  fun missingExactPermissionUsesFallbackOnAndroidTwelveAndLater() {
    assertEquals(
      AlarmScheduleMode.INEXACT_FALLBACK,
      AlarmModeSelector.select(sdkInt = 31, exactPermissionAvailable = false),
    )
    assertEquals(
      AlarmScheduleMode.EXACT,
      AlarmModeSelector.select(sdkInt = 30, exactPermissionAvailable = false),
    )
  }

  @Test
  fun callerCanFilterPastAlarmBeforePlanning() {
    val now = 5_000L
    val candidates = listOf(
      record("past", start = 4_000),
      record("future", start = 6_000),
    )
    val future = candidates.filter { it.startsAtEpochMillis > now }
    assertEquals(listOf("future"), future.map { it.occurrenceId })
  }

  private fun record(
    id: String,
    start: Long = 2_000,
    requestCode: Int = 42,
  ) = LessonAlarmRecord(
    occurrenceId = id,
    title = "Matematik",
    startsAtEpochMillis = start,
    endsAtEpochMillis = start + 1_000,
    requestCode = requestCode,
  )
}
