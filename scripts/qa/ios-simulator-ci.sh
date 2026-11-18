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
suite="${ACADEMY_IOS_SUITE:-role-homes}"
case "$suite" in
  role-homes) target=integration_test/cloud_role_homes_test.dart ;;
  enrollment-proof)
    target=integration_test/cloud_enrollment_proof_test.dart
    export Demo__SeedProfile=LegacyRegression
    ;;
  *) echo 'Unknown isolated iOS suite.' >&2; exit 1 ;;
esac
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

if [[ "$suite" == enrollment-proof ]]; then
  # Fixture changes only the disposable database created above. Never accept a
  # connection/database from a caller or copy any production/dump identity.
  psql -h 127.0.0.1 -p 55432 -U academy_ci -d "$db_name" -v ON_ERROR_STOP=1 \
    -f scripts/qa/ios-proof-fixture.sql > "$qa_dir/evidence/proof-fixture.log"
  psql -h 127.0.0.1 -p 55432 -U academy_ci -d "$db_name" -At -v ON_ERROR_STOP=1 \
    -f scripts/qa/ios-proof-financial-snapshot.sql > "$qa_dir/evidence/money-before.json"
fi

echo 'Starting one disposable iOS 26.2 simulator.'
xcrun simctl list runtimes > "$qa_dir/evidence/runtimes.log"
simulator=$(xcrun simctl create 'Academy QA iOS' com.apple.CoreSimulator.SimDeviceType.iPhone-16e com.apple.CoreSimulator.SimRuntime.iOS-26-2)
gtimeout --kill-after=20s 8m xcrun simctl bootstatus "$simulator" -b
xcrun simctl status_bar "$simulator" override --time '9:41' --dataNetwork wifi --wifiMode active --wifiBars 3 --cellularMode active --cellularBars 4 --batteryState discharging --batteryLevel 100 || echo 'The simulator kept its default status bar.' >&2

cd apps/mobile
echo 'Building the Flutter test application for iOS.'
gtimeout --kill-after=20s 12m flutter build ios --simulator --debug --no-pub \
  --target="$target" \
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
  --target="$target" \
  --no-publish-port --timeout=300 --screenshot="$qa_dir/evidence" \
  2>&1 | tee "$qa_dir/evidence/flutter-ios.log" || test_status=$?

if [[ "$test_status" != 0 ]]; then
  gtimeout 20s xcrun simctl io "$simulator" screenshot "$qa_dir/evidence/ios-last-frame.png" || true
fi

if [[ "$suite" == enrollment-proof ]]; then
  for capture in ../../tmp/mobile-visuals/phase-iosproof-*.png ../../tmp/mobile-visuals/ios-cloud-response.json; do
    if [[ -s "$capture" ]]; then cp "$capture" "$qa_dir/evidence/"; fi
  done
  psql -h 127.0.0.1 -p 55432 -U academy_ci -d "$db_name" -At -v ON_ERROR_STOP=1 \
    -f ../../scripts/qa/ios-proof-financial-snapshot.sql > "$qa_dir/evidence/money-after.json"
  if cmp -s "$qa_dir/evidence/money-before.json" "$qa_dir/evidence/money-after.json"; then
    echo 'IP22: all collections, receipts and subscription periods unchanged.' | tee "$qa_dir/evidence/money-comparison.log"
  else
    echo 'IP22: financial rows changed.' | tee "$qa_dir/evidence/money-comparison.log" >&2
    test_status=1
  fi
  if [[ "$test_status" == 0 ]]; then
    python3 - "$qa_dir/evidence" <<'PYREPORT'
import hashlib, json, pathlib, sys
folder = pathlib.Path(sys.argv[1])
report = json.loads((folder / 'ios-cloud-response.json').read_text())
proof = report['proofReview']
expected = {f'IP{i:02}' for i in range(1, 25)} - {'IP20', 'IP22'}
assert set(proof['executedPassed']) == expected
assert proof['businessWritesAfterFixture'] == []
assert set(proof['successfulAssetHashes']) == {proof['originalPngSha256']}
assert len(list(folder.glob('phase-iosproof-*.png'))) == 9
money = json.loads((folder / 'money-after.json').read_text())
summary = {'executedAutomated': sorted(expected | {'IP22'}),
           'visualReviewPending': ['IP20'],
           'financialRows': {key: len(value) for key, value in money.items()},
           'financialSha256': hashlib.sha256((folder / 'money-after.json').read_bytes()).hexdigest()}
(folder / 'proof-summary.json').write_text(json.dumps(summary, indent=2))
print(json.dumps(summary))
PYREPORT
  fi
else
for capture in ../../tmp/mobile-visuals/phase-cloud-*-incomplete.png ../../tmp/mobile-visuals/phase-cloud-admin-initial-error.png; do
  if [[ -s "$capture" ]]; then cp "$capture" "$qa_dir/evidence/"; fi
done
for role in owner admin coach guardian; do
  if [[ -s "../../tmp/mobile-visuals/phase-cloud-$role.png" ]]; then
    cp "../../tmp/mobile-visuals/phase-cloud-$role.png" "$qa_dir/evidence/"
  elif [[ "$test_status" == 0 ]]; then
    echo "Missing simulator evidence for $role." >&2
    test_status=1
  fi
done
fi
exit "$test_status"
