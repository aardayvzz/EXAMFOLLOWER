package com.examfollower.student.notifications

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build

class LessonAlarmScheduler(private val context: Context) {
  private val alarmManager = context.getSystemService(AlarmManager::class.java)

  fun exactAlarmAvailable(): Boolean =
    Build.VERSION.SDK_INT < Build.VERSION_CODES.S || alarmManager.canScheduleExactAlarms()

  fun schedule(record: LessonAlarmRecord): AlarmScheduleMode {
    require(record.startsAtEpochMillis > System.currentTimeMillis()) {
      "Past alarms cannot be scheduled"
    }
    val mode = AlarmModeSelector.select(Build.VERSION.SDK_INT, exactAlarmAvailable())
    val operation = requireNotNull(pendingIntent(record, PendingIntent.FLAG_UPDATE_CURRENT)) {
      "Lesson alarm PendingIntent could not be created"
    }
    when (mode) {
      AlarmScheduleMode.EXACT -> {
        try {
          if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            alarmManager.setExactAndAllowWhileIdle(
              AlarmManager.RTC_WAKEUP,
              record.startsAtEpochMillis,
              operation,
            )
          } else {
            alarmManager.setExact(
              AlarmManager.RTC_WAKEUP,
              record.startsAtEpochMillis,
              operation,
            )
          }
        } catch (_: SecurityException) {
          alarmManager.setAndAllowWhileIdle(
            AlarmManager.RTC_WAKEUP,
            record.startsAtEpochMillis,
            operation,
          )
          return AlarmScheduleMode.INEXACT_FALLBACK
        }
      }
      AlarmScheduleMode.INEXACT_FALLBACK -> alarmManager.setAndAllowWhileIdle(
        AlarmManager.RTC_WAKEUP,
        record.startsAtEpochMillis,
        operation,
      )
    }
    return mode
  }

  fun cancel(record: LessonAlarmRecord) {
    val operation = pendingIntent(record, PendingIntent.FLAG_NO_CREATE) ?: return
    alarmManager.cancel(operation)
    operation.cancel()
  }

  private fun pendingIntent(record: LessonAlarmRecord, behaviorFlag: Int): PendingIntent? {
    val intent = Intent(context, LessonAlarmReceiver::class.java).apply {
      data = Uri.Builder()
        .scheme("examfollower")
        .authority("lesson")
        .appendPath(record.occurrenceId)
        .build()
      putExtra(LessonAlarmReceiver.EXTRA_OCCURRENCE_ID, record.occurrenceId)
      putExtra(LessonAlarmReceiver.EXTRA_TITLE, record.title)
      putExtra(LessonAlarmReceiver.EXTRA_STARTS_AT, record.startsAtEpochMillis)
      putExtra(LessonAlarmReceiver.EXTRA_REQUEST_CODE, record.requestCode)
    }
    return PendingIntent.getBroadcast(
      context,
      record.requestCode,
      intent,
      behaviorFlag or PendingIntent.FLAG_IMMUTABLE,
    )
  }
}
