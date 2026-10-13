package com.examfollower.student.notifications

import java.nio.ByteBuffer
import java.nio.charset.StandardCharsets
import java.security.MessageDigest

data class LessonAlarmRecord(
  val occurrenceId: String,
  val title: String,
  val startsAtEpochMillis: Long,
  val endsAtEpochMillis: Long,
  val requestCode: Int,
)

data class LessonAlarmSnapshot(
  val validUntilEpochMillis: Long,
  val records: List<LessonAlarmRecord>,
)

data class AlarmPlan(
  val added: List<LessonAlarmRecord>,
  val changed: List<LessonAlarmRecord>,
  val removed: List<LessonAlarmRecord>,
  val unchanged: List<LessonAlarmRecord>,
) {
  companion object {
    fun create(
      previous: List<LessonAlarmRecord>,
      next: List<LessonAlarmRecord>,
    ): AlarmPlan {
      val oldById = previous.associateBy { it.occurrenceId }
      val newById = next.associateBy { it.occurrenceId }
      val added = mutableListOf<LessonAlarmRecord>()
      val changed = mutableListOf<LessonAlarmRecord>()
      val unchanged = mutableListOf<LessonAlarmRecord>()

      next.forEach { current ->
        val old = oldById[current.occurrenceId]
        when {
          old == null -> added += current
          old != current -> changed += current
          else -> unchanged += current
        }
      }

      return AlarmPlan(
        added = added,
        changed = changed,
        removed = previous.filter { it.occurrenceId !in newById },
        unchanged = unchanged,
      )
    }
  }
}

enum class AlarmScheduleMode { EXACT, INEXACT_FALLBACK }

object AlarmModeSelector {
  fun select(sdkInt: Int, exactPermissionAvailable: Boolean): AlarmScheduleMode =
    if (sdkInt < 31 || exactPermissionAvailable) {
      AlarmScheduleMode.EXACT
    } else {
      AlarmScheduleMode.INEXACT_FALLBACK
    }
}

object StableAlarmId {
  fun assign(
    occurrenceIds: Collection<String>,
    previous: Collection<LessonAlarmRecord>,
  ): Map<String, Int> {
    val requested = occurrenceIds.toSet()
    val result = linkedMapOf<String, Int>()
    val used = mutableSetOf<Int>()

    previous.sortedBy { it.occurrenceId }.forEach { old ->
      if (old.occurrenceId in requested && used.add(old.requestCode)) {
        result[old.occurrenceId] = old.requestCode
      }
    }

    requested.sorted().forEach { occurrenceId ->
      if (occurrenceId !in result) {
        var candidate = initialCode(occurrenceId)
        while (!used.add(candidate)) {
          candidate = if (candidate == Int.MAX_VALUE) 0 else candidate + 1
        }
        result[occurrenceId] = candidate
      }
    }
    return result
  }

  private fun initialCode(occurrenceId: String): Int {
    val digest = MessageDigest.getInstance("SHA-256")
      .digest(occurrenceId.toByteArray(StandardCharsets.UTF_8))
    return ByteBuffer.wrap(digest, 0, Int.SIZE_BYTES).int and Int.MAX_VALUE
  }
}
