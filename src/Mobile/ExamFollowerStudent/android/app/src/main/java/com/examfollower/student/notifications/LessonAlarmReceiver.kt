package com.examfollower.student.notifications

import android.Manifest
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.examfollower.student.MainActivity
import com.examfollower.student.R
import java.text.DateFormat
import java.util.Date

class LessonAlarmReceiver : BroadcastReceiver() {
  override fun onReceive(context: Context, intent: Intent) {
    val occurrenceId = intent.getStringExtra(EXTRA_OCCURRENCE_ID) ?: return
    val title = intent.getStringExtra(EXTRA_TITLE)?.takeIf { it.isNotBlank() } ?: return
    val startsAt = intent.getLongExtra(EXTRA_STARTS_AT, 0L).takeIf { it > 0L } ?: return

    NotificationChannels.ensureCreated(context)
    if (
      Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
      context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) !=
        PackageManager.PERMISSION_GRANTED
    ) {
      removeFromSnapshot(context, occurrenceId)
      return
    }

    val openApp = Intent(context, MainActivity::class.java).apply {
      flags = Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
    }
    val contentIntent = PendingIntent.getActivity(
      context,
      0,
      openApp,
      PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
    )
    val localTime = DateFormat.getTimeInstance(DateFormat.SHORT).format(Date(startsAt))
    val notification = NotificationCompat.Builder(context, NotificationChannels.LESSON_CHANNEL_ID)
      .setSmallIcon(R.mipmap.ic_launcher)
      .setContentTitle("Dersiniz başladı")
      .setContentText("$title • $localTime")
      .setPriority(NotificationCompat.PRIORITY_HIGH)
      .setAutoCancel(true)
      .setContentIntent(contentIntent)
      .build()
    val notificationId = intent.getIntExtra(EXTRA_REQUEST_CODE, 0)
    NotificationManagerCompat.from(context).notify(notificationId, notification)
    removeFromSnapshot(context, occurrenceId)
  }

  private fun removeFromSnapshot(context: Context, occurrenceId: String) {
    val store = LessonAlarmStore(context)
    runCatching {
      val snapshot = store.loadSnapshot()
      store.save(
        snapshot.records.filterNot { it.occurrenceId == occurrenceId },
        snapshot.validUntilEpochMillis,
      )
    }
  }

  companion object {
    const val EXTRA_OCCURRENCE_ID = "occurrenceId"
    const val EXTRA_TITLE = "title"
    const val EXTRA_STARTS_AT = "startsAtEpochMillis"
    const val EXTRA_REQUEST_CODE = "requestCode"
  }
}
