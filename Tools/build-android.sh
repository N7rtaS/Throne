#!/bin/zsh
# Build the Android APK.
# Android tools (Gradle, NDK) do not accept non-ASCII paths, and the project lives in "Терновый Трон".
# The Unity project is mirrored to an ASCII staging folder (kept between builds so Library is reused),
# built there, and the APK and report are copied back to Builds/Android and Research.
# Release APK (no Development Build) by default; extra arguments go to Unity, e.g. -thornDevelopment for a
# development APK (profiler). Signing key from THORN_KEYSTORE, THORN_KEYSTORE_PASS, THORN_KEY_ALIAS, THORN_KEY_PASS;
# without them the APK is signed with the Unity debug key (not valid for acceptance).
set -euo pipefail
root="${0:A:h:h}"
unity="${THORN_UNITY:-/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity}"
stage="${THORN_ANDROID_STAGE:-$HOME/Library/Caches/ThornThroneAndroid}"

mkdir -p "$stage/Research" "$stage/Builds/Android"
rsync -a --delete \
  --exclude Library/ --exclude Temp/ --exclude Logs/ --exclude UserSettings/ --exclude obj/ \
  "$root/UnityProject/" "$stage/UnityProject/"

"$unity" -batchmode -nographics -quit -buildTarget Android \
  -projectPath "$stage/UnityProject" \
  -executeMethod ThornThrone.Editor.MobileBuilder.BuildAndroid \
  -logFile "$root/Research/android-build.log" \
  "$@"
grep -q THORN_ANDROID_BUILD_OK "$root/Research/android-build.log" || { echo "build-android: no THORN_ANDROID_BUILD_OK in Research/android-build.log" >&2; exit 1; }
grep -h "THORN_ANDROID_BUILD_OPTIONS\|THORN_ANDROID_DEBUG_SIGNED" "$root/Research/android-build.log" || true

mkdir -p "$root/Builds/Android"
cp "$stage/Builds/Android/ThornThrone.apk" "$root/Builds/Android/ThornThrone.apk"
cp "$stage/Research/android-build-report.json" "$root/Research/android-build-report.json"
echo "APK: $root/Builds/Android/ThornThrone.apk"
