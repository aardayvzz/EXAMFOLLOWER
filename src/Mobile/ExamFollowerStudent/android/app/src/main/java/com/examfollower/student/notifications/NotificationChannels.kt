package com.examfollower.student.notifications

import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.os.Build

object NotificationChannels {
  const val LESSON_CHANNEL_ID = "lesson_start_v1"

  fun ensureCreated(context: Context) {
    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
    val manager = context.getSystemService(NotificationManager::class.java)
    if (manager.getNotificationChannel(LESSON_CHANNEL_ID) != null) return

    val channel = NotificationChannel(
      LESSON_CHANNEL_ID,
      "Ders başlangıç bildirimleri",
      NotificationManager.IMPORTANCE_HIGH,
    ).apply {
      description = "Ders başlangıç saatlerinde gösterilen uyarılar"
      enableVibration(true)
    }
    manager.createNotificationChannel(channel)
  }
}
