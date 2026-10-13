package com.examfollower.student.notifications

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

class BootReceiver : BroadcastReceiver() {
  override fun onReceive(context: Context, intent: Intent) {
    if (
      intent.action != Intent.ACTION_BOOT_COMPLETED &&
      intent.action != Intent.ACTION_MY_PACKAGE_REPLACED
    ) return

    val store = LessonAlarmStore(context)
    val scheduler = LessonAlarmScheduler(context)
    val snapshot = store.loadSnapshot()
    val now = System.currentTimeMillis()
    if (snapshot.validUntilEpochMillis <= now) {
      snapshot.records.forEach { runCatching { scheduler.cancel(it) } }
      runCatching { store.clear() }
      return
    }
    val future = snapshot.records.filter { it.startsAtEpochMillis > now }
    future.forEach { record ->
      try {
        scheduler.schedule(record)
      } catch (_: RuntimeException) {
        // A later foreground synchronization can safely retry this occurrence.
      }
    }
    runCatching { store.save(future, snapshot.validUntilEpochMillis) }
  }
}
