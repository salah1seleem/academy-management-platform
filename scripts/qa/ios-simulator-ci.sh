#!/usr/bin/env bash
set -euo pipefail

: "${RUNNER_TEMP:?Requires a GitHub-hosted QA runner}"
if [[ "${GITHUB_ACTIONS:-}" != true || "${RUNNER_ENVIRONMENT:-}" != github-hosted || "$(uname -s)" != Darwin ]]; then
  echo 'Run this job on a GitHub-hosted macOS runner.' >&2
  exit 1
fi

qa_dir="$RUNNER_TEMP/academy-ios-qa"
mkdir -p "$qa_dir/evidence" "$qa_dir/socket"
export ConnectionStrings__Default='Host=127.0.0.1;Port=55432;Database=academy_ios_qa;Username=academy_ci'
export ASPNETCORE_ENVIRONMENT=Demo
export ASPNETCORE_URLS=http://127.0.0.1:5397
export Demo__SeedProfile=Football Demo__SeedEnabled=true Demo__StaffPassword=Demo-Only-123!
export Demo__FixedOtpEnabled=true Demo__FixedOtp=246810
export Media__PrivateRoot="$qa_dir/media" DataProtection__KeyPath="$qa_dir/keys"
export Logging__LogLevel__Default=Warning Logging__LogLevel__Microsoft__EntityFrameworkCore__Database__Command=Warning

simulator=''
api_pid=''
cleanup() {
  if [[ -n "$simulator" ]]; then
    gtimeout 45s xcrun simctl shutdown "$simulator" || true
    gtimeout 45s xcrun simctl delete "$simulator" || true
  fi
  if [[ -n "$api_pid" ]]; then
    kill "$api_pid" 2>/dev/null || true
    wait "$api_pid" 2>/dev/null || true
  fi
  if [[ -f "$qa_dir/postgres/postmaster.pid" ]]; then
    pg_ctl -D "$qa_dir/postgres" -m fast stop || true
  fi
}
trap cleanup EXIT

initdb -D "$qa_dir/postgres" -U academy_ci --auth=trust > "$qa_dir/evidence/postgres-init.log"
pg_ctl -D "$qa_dir/postgres" -l "$qa_dir/evidence/postgres.log" -o "-h 127.0.0.1 -p 55432 -k $qa_dir/socket" -w start
createdb -h 127.0.0.1 -p 55432 -U academy_ci academy_ios_qa

echo 'Building the isolated Demo API and applying migrations.'
dotnet tool restore
dotnet build apps/api/src/Academy.Api -c Release -m:1 > "$qa_dir/evidence/api-build.log" 2>&1
dotnet ef database update --project apps/api/src/Academy.Infrastructure --startup-project apps/api/src/Academy.Api --no-build --configuration Release > "$qa_dir/evidence/migrate.log" 2>&1
dotnet apps/api/src/Academy.Api/bin/Release/net10.0/Academy.Api.dll --contentRoot "$PWD/apps/api/src/Academy.Api" > "$qa_dir/evidence/api.log" 2>&1 &
api_pid=$!
for (( attempt=0; attempt<120; attempt++ )); do
  if curl -fsS --max-time 2 "$ASPNETCORE_URLS/health/ready" > "$qa_dir/evidence/ready.json" 2>/dev/null; then break; fi
  if ! kill -0 "$api_pid" 2>/dev/null; then
    tail -60 "$qa_dir/evidence/api.log" >&2
    exit 1
  fi
  sleep 1
done
curl -fsS --max-time 2 "$ASPNETCORE_URLS/health/ready" > "$qa_dir/evidence/ready.json"

echo 'Starting one disposable iOS 26.2 simulator.'
xcrun simctl list runtimes > "$qa_dir/evidence/runtimes.log"
simulator=$(xcrun simctl create 'Academy QA iOS' com.apple.CoreSimulator.SimDeviceType.iPhone-16e com.apple.CoreSimulator.SimRuntime.iOS-26-2)
gtimeout --kill-after=20s 8m xcrun simctl bootstatus "$simulator" -b
xcrun simctl status_bar "$simulator" override --time '9:41' --dataNetwork wifi --wifiMode active --wifiBars 3 --cellularMode active --cellularBars 4 --batteryState discharging --batteryLevel 100 || echo 'The simulator kept its default status bar.' >&2

cd apps/mobile
echo 'Building the Flutter test application for iOS.'
gtimeout --kill-after=20s 12m flutter build ios --simulator --debug --no-pub \
  --target=integration_test/cloud_role_homes_test.dart \
  --dart-define=APP_ENV=Demo \
  --dart-define=ACADEMY_ISOLATED_IOS_QA=true \
  --dart-define=API_BASE_URL="$ASPNETCORE_URLS" \
  2>&1 | tee "$qa_dir/evidence/flutter-ios-build.log"

echo 'Installing and launching the iOS test app.'
test_status=0
ACADEMY_VISUAL_RUN=ios-cloud gtimeout --kill-after=20s 7m flutter drive --verbose --no-pub \
  -d "$simulator" \
  --use-application-binary=build/ios/iphonesimulator/Runner.app \
  --driver=test_driver/visual_driver.dart \
  --target=integration_test/cloud_role_homes_test.dart \
  --no-publish-port --timeout=240 --screenshot="$qa_dir/evidence" \
  2>&1 | tee "$qa_dir/evidence/flutter-ios.log" || test_status=$?

if [[ "$test_status" != 0 ]]; then
  gtimeout 20s xcrun simctl io "$simulator" screenshot "$qa_dir/evidence/ios-last-frame.png" || true
fi

for role in owner admin coach guardian; do
  if [[ -s "../../tmp/mobile-visuals/phase-cloud-$role.png" ]]; then
    cp "../../tmp/mobile-visuals/phase-cloud-$role.png" "$qa_dir/evidence/"
  elif [[ "$test_status" == 0 ]]; then
    echo "Missing simulator evidence for $role." >&2
    test_status=1
  fi
done
exit "$test_status"
