import assert from 'node:assert/strict';
import test from 'node:test';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const harnessRoot = dirname(dirname(fileURLToPath(import.meta.url)));
const evidenceModulePath = join(harnessRoot, 'lib', 'evidence.mjs');
const scenariosPath = join(harnessRoot, 'scenarios.json');

function requiredFile(path, description) {
  assert.equal(existsSync(path), true, description);
}

test('P16-A evidence module is present and exposes the required pure result contracts', async () => {
  requiredFile(evidenceModulePath, 'The P16-A evidence module must exist.');

  const evidence = await import(pathToFileURL(evidenceModulePath));
  assert.equal(typeof evidence.createSummary, 'function');
  assert.equal(typeof evidence.recordScreenshot, 'function');
  assert.equal(typeof evidence.finalizeOwnedResources, 'function');

  const summary = evidence.createSummary({
    scenarioId: 'P16A-SettingsReleaseNotesNavigation',
    matrixMapping: null,
    result: 'Passed',
    failedStep: null,
    git: { commit: 'abc', branch: 'feature/p16a-android-gui-foundation-v1' },
    buildIdentity: { version: '1.0.0-beta.12', build: '12' },
    packageId: 'com.tachiguro.knownfirst.guitest',
    configuration: 'Debug',
    toolVersions: {},
    device: {},
    physicalOrEmulator: 'emulator',
    orientation: 'PORTRAIT',
    screenshotPixels: { width: 412, height: 915 },
    density: 1,
    dpViewport: { width: 412, height: 915 },
    language: 'en',
    theme: 'light',
    contexts: { native: 'NATIVE_APP', webview: 'WEBVIEW_com.tachiguro.knownfirst.guitest' },
    profileId: 'test-profile',
    safetyBefore: { passed: true },
    safetyAfter: { passed: true },
    timestamps: { startedAtUtc: '2026-08-11T00:00:00.000Z', endedAtUtc: '2026-08-11T00:00:01.000Z' },
    assertionCounts: { passed: 1, failed: 0 },
    remainingUnproven: ['runtime execution']
  });
  assert.equal(summary.matrixMapping, null);
  assert.equal(summary.buildPerformed, false);
  assert.equal(summary.installationPerformed, false);
  assert.equal(summary.dataResetPerformed, false);
  assert.equal(summary.liveNetworkUsed, false);

  const withScreenshot = evidence.recordScreenshot(summary, {
    name: 'release-notes.png',
    bytes: Buffer.from('evidence')
  });
  assert.equal(withScreenshot.screenshots[0].name, 'release-notes.png');
  assert.match(withScreenshot.screenshots[0].sha256, /^[a-f0-9]{64}$/);
});

test('P16-A evidence rejects incomplete metadata and safety failures override scenario success', async () => {
  requiredFile(evidenceModulePath, 'The P16-A evidence module must exist.');
  const evidence = await import(pathToFileURL(evidenceModulePath));

  assert.throws(() => evidence.createSummary({ scenarioId: 'P16A-SettingsReleaseNotesNavigation' }), /missing/i);
  const result = await evidence.finalizeOwnedResources({
    scenarioSucceeded: true,
    safetyAfter: { passed: false },
    session: { quit: async () => undefined },
    ownedServer: { terminate: async () => undefined }
  });
  assert.equal(result.succeeded, false);
  assert.equal(result.sessionClosed, true);
  assert.equal(result.serverTerminated, true);
});

test('P16-A scenario registry is exactly the approved pre-matrix scenario', () => {
  requiredFile(scenariosPath, 'The P16-A Android scenario registry must exist.');

  const registry = JSON.parse(readFileSync(scenariosPath, 'utf8'));
  assert.equal(registry.scenarios.length, 1);
  assert.deepEqual(registry.scenarios[0], {
    id: 'P16A-SettingsReleaseNotesNavigation',
    matrixMapping: null,
    relatedMatrixRow: 'S36',
    implementation: 'scenarios/settings-release-notes-navigation.mjs'
  });
});
