#!/bin/zsh
# Build the Android APK.
# Android tools (Gradle, NDK) do not accept non-ASCII paths, and the project lives in "Терновый Трон".
# The Unity project is mirrored to an ASCII staging folder (kept between builds so Library is reused),
# built there, and the APK and report are copied back to Builds/Android and Research.
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
  -logFile "$root/Research/android-build.log"

mkdir -p "$root/Builds/Android"
cp "$stage/Builds/Android/ThornThrone.apk" "$root/Builds/Android/ThornThrone.apk"
cp "$stage/Research/android-build-report.json" "$root/Research/android-build-report.json"
echo "APK: $root/Builds/Android/ThornThrone.apk"
