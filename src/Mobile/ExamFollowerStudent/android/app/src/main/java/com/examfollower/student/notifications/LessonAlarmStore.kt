package com.examfollower.student.notifications

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

class LessonAlarmStore(context: Context) {
  private val preferences = context.getSharedPreferences(
    PREFERENCES_NAME,
    Context.MODE_PRIVATE,
  )

  fun loadSnapshot(): LessonAlarmSnapshot {
    val raw = preferences.getString(SNAPSHOT_KEY, null) ?: return EMPTY_SNAPSHOT
    return try {
      val root = JSONObject(raw)
      val array = root.getJSONArray("records")
      val records = buildList {
        for (index in 0 until array.length()) {
          val item = array.getJSONObject(index)
          add(
            LessonAlarmRecord(
              occurrenceId = item.getString("occurrenceId"),
              title = item.getString("title"),
              startsAtEpochMillis = item.getLong("startsAtEpochMillis"),
              endsAtEpochMillis = item.getLong("endsAtEpochMillis"),
              requestCode = item.getInt("requestCode"),
            ),
          )
        }
      }
      LessonAlarmSnapshot(root.getLong("validUntilEpochMillis"), records)
    } catch (_: Exception) {
      EMPTY_SNAPSHOT
    }
  }

  fun load(): List<LessonAlarmRecord> = loadSnapshot().records

  fun save(records: List<LessonAlarmRecord>, validUntilEpochMillis: Long) {
    val array = JSONArray()
    records.forEach { record ->
      array.put(
        JSONObject()
          .put("occurrenceId", record.occurrenceId)
          .put("title", record.title)
          .put("startsAtEpochMillis", record.startsAtEpochMillis)
          .put("endsAtEpochMillis", record.endsAtEpochMillis)
          .put("requestCode", record.requestCode),
      )
    }
    val root = JSONObject()
      .put("validUntilEpochMillis", validUntilEpochMillis)
      .put("records", array)
    check(preferences.edit().putString(SNAPSHOT_KEY, root.toString()).commit()) {
      "Lesson alarm snapshot could not be stored"
    }
  }

  fun clear() {
    check(preferences.edit().remove(SNAPSHOT_KEY).commit()) {
      "Lesson alarm snapshot could not be cleared"
    }
  }

  companion object {
    private const val PREFERENCES_NAME = "lesson_alarm_snapshot"
    private const val SNAPSHOT_KEY = "occurrences"
    private val EMPTY_SNAPSHOT = LessonAlarmSnapshot(0L, emptyList())
  }
}
