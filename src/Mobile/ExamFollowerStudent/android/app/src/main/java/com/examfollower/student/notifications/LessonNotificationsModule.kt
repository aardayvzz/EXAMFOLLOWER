package com.examfollower.student.notifications

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import com.facebook.react.bridge.Arguments
import com.facebook.react.bridge.Promise
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.bridge.ReactMethod
import com.facebook.react.bridge.ReadableArray

class LessonNotificationsModule(
  private val reactContext: ReactApplicationContext,
) : ReactContextBaseJavaModule(reactContext) {
  private val store = LessonAlarmStore(reactContext)
  private val scheduler = LessonAlarmScheduler(reactContext)

  override fun getName(): String = "LessonNotifications"

  @ReactMethod
  fun synchronize(
    items: ReadableArray,
    validUntilEpochMillis: Double,
    promise: Promise,
  ) {
    val now = System.currentTimeMillis()
    val validUntil = validUntilEpochMillis.toLong()
    if (!validUntilEpochMillis.isFinite() || validUntil <= now) {
      promise.reject("INVALID_SNAPSHOT", "Ders listesinin geçerlilik süresi dolmuş.")
      return
    }
    val previous = store.load()
    val parsed = mutableListOf<UnassignedAlarm>()
    val seen = mutableSetOf<String>()
    var invalidCount = 0

    for (index in 0 until items.size()) {
      try {
        val item = items.getMap(index) ?: throw IllegalArgumentException()
        val occurrenceId = item.getString("occurrenceId")?.trim().orEmpty()
        val title = item.getString("title")?.trim().orEmpty()
        val startsAt = item.getDouble("startsAtEpochMillis").toLong()
        val endsAt = item.getDouble("endsAtEpochMillis").toLong()
        if (
          occurrenceId.isEmpty() || occurrenceId.length > 256 ||
          title.isEmpty() || title.length > 200 ||
          startsAt <= now || endsAt <= startsAt || !seen.add(occurrenceId)
        ) {
          throw IllegalArgumentException()
        }
        parsed += UnassignedAlarm(occurrenceId, title, startsAt, endsAt)
      } catch (_: Exception) {
        invalidCount += 1
      }
    }

    val ids = StableAlarmId.assign(parsed.map { it.occurrenceId }, previous)
    val next = parsed.map { item ->
      LessonAlarmRecord(
        occurrenceId = item.occurrenceId,
        title = item.title,
        startsAtEpochMillis = item.startsAtEpochMillis,
        endsAtEpochMillis = item.endsAtEpochMillis,
        requestCode = ids.getValue(item.occurrenceId),
      )
    }
    val plan = AlarmPlan.create(previous, next)

    try {
      val changedOld = previous.filter { old ->
        plan.changed.any { it.occurrenceId == old.occurrenceId }
      }
      (plan.removed + changedOld).forEach(scheduler::cancel)
      (plan.added + plan.changed).forEach(scheduler::schedule)
      store.save(next, validUntil)
      promise.resolve(
        summaryMap(
          scheduledCount = next.size,
          addedCount = plan.added.size,
          changedCount = plan.changed.size,
          removedCount = plan.removed.size,
          invalidCount = invalidCount,
        ),
      )
    } catch (error: Exception) {
      rollback(previous, next)
      promise.reject("ALARM_SYNC_FAILED", "Ders alarmları planlanamadı.", error)
    }
  }

  @ReactMethod
  fun cancelAll(promise: Promise) {
    try {
      store.load().forEach(scheduler::cancel)
      store.clear()
      promise.resolve(null)
    } catch (error: Exception) {
      promise.reject("ALARM_CLEAR_FAILED", "Ders alarmları temizlenemedi.", error)
    }
  }

  @ReactMethod
  fun getSummary(promise: Promise) {
    val snapshot = store.loadSnapshot()
    val now = System.currentTimeMillis()
    if (snapshot.validUntilEpochMillis in 1..now) {
      snapshot.records.forEach { runCatching { scheduler.cancel(it) } }
      runCatching { store.clear() }
      promise.resolve(summaryMap(scheduledCount = 0))
      return
    }
    val futureCount = snapshot.records.count { it.startsAtEpochMillis > now }
    promise.resolve(summaryMap(scheduledCount = futureCount))
  }

  @ReactMethod
  fun openExactAlarmSettings(promise: Promise) {
    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S) {
      promise.resolve(false)
      return
    }
    try {
      val intent = Intent(
        Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM,
        Uri.parse("package:${reactContext.packageName}"),
      ).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
      reactContext.startActivity(intent)
      promise.resolve(true)
    } catch (error: Exception) {
      promise.reject("SETTINGS_UNAVAILABLE", "Alarm ayarları açılamadı.", error)
    }
  }

  private fun rollback(
    previous: List<LessonAlarmRecord>,
    attempted: List<LessonAlarmRecord>,
  ) {
    attempted.forEach { runCatching { scheduler.cancel(it) } }
    previous.filter { it.startsAtEpochMillis > System.currentTimeMillis() }
      .forEach { runCatching { scheduler.schedule(it) } }
  }

  private fun summaryMap(
    scheduledCount: Int,
    addedCount: Int = 0,
    changedCount: Int = 0,
    removedCount: Int = 0,
    invalidCount: Int = 0,
  ) = Arguments.createMap().apply {
    val exactAvailable = scheduler.exactAlarmAvailable()
    putInt("scheduledCount", scheduledCount)
    putInt("addedCount", addedCount)
    putInt("changedCount", changedCount)
    putInt("removedCount", removedCount)
    putInt("invalidCount", invalidCount)
    putBoolean("exactAlarmAvailable", exactAvailable)
    putBoolean("fallbackUsed", scheduledCount > 0 && !exactAvailable)
    putBoolean("notificationPermissionGranted", notificationPermissionGranted())
  }

  private fun notificationPermissionGranted(): Boolean =
    Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU ||
      reactContext.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) ==
      PackageManager.PERMISSION_GRANTED

  private data class UnassignedAlarm(
    val occurrenceId: String,
    val title: String,
    val startsAtEpochMillis: Long,
    val endsAtEpochMillis: Long,
  )
}
