package com.examfollower.student.notifications

import com.examfollower.student.BuildConfig
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule

class AppConfigModule(reactContext: ReactApplicationContext) :
  ReactContextBaseJavaModule(reactContext) {
  override fun getName(): String = "AppConfig"

  override fun getConstants(): Map<String, Any> = mapOf(
    "apiBaseUrl" to BuildConfig.API_BASE_URL,
  )
}
