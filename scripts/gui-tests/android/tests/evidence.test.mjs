import assert from 'node:assert/strict';
import test from 'node:test';
import {
  existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync
} from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const harnessRoot = dirname(dirname(fileURLToPath(import.meta.url)));
const evidenceModulePath = join(harnessRoot, 'lib', 'evidence.mjs');
const scenariosPath = join(harnessRoot, 'scenarios.json');
const repositoryRoot = dirname(dirname(dirname(harnessRoot)));
const runsRoot = join(repositoryRoot, 'artifacts', 'gui-tests', 'android', 'runs');
const fullCommit = '0123456789abcdef0123456789abcdef01234567';

function requiredFile(path, description) {
  assert.equal(existsSync(path), true, description);
}

function summaryInput(buildIdentity) {
  return {
    scenarioId: 'P16A-SettingsReleaseNotesNavigation',
    matrixMapping: null,
    result: 'Passed',
    failedStep: null,
    git: { commit: fullCommit, branch: 'feature/p16a-android-gui-foundation-v1' },
    buildIdentity,
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
  };
}

test('P16-A evidence module is present and exposes the required pure result contracts', async () => {
  requiredFile(evidenceModulePath, 'The P16-A evidence module must exist.');

  const evidence = await import(pathToFileURL(evidenceModulePath));
  assert.equal(typeof evidence.createSummary, 'function');
  assert.equal(typeof evidence.recordScreenshot, 'function');
  assert.equal(typeof evidence.finalizeOwnedResources, 'function');

  const summary = evidence.createSummary(summaryInput({
    expected: { commit: fullCommit },
    observed: {
      commit: fullCommit,
      dirty: 'false',
      version: '1.0.0-beta.12',
      buildNumber: '12',
      configuration: 'Debug',
      packageId: 'com.tachiguro.knownfirst.guitest'
    },
    matched: true,
    failureReason: null
  }));
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

test('P16-A runtime identity requires a clean exact full-SHA match before pass evidence', async () => {
  const evidence = await import(pathToFileURL(evidenceModulePath));
  assert.equal(typeof evidence.evaluateRuntimeBuildIdentity, 'function');

  const observed = {
    commit: fullCommit.toUpperCase(),
    dirty: 'false',
    version: '1.0.0-beta.12',
    buildNumber: '12',
    configuration: 'Debug',
    packageId: 'com.tachiguro.knownfirst.guitest'
  };
  const matched = evidence.evaluateRuntimeBuildIdentity({ expectedCommit: fullCommit, observed });
  assert.equal(matched.matched, true);
  assert.equal(matched.failureReason, null);
  assert.notStrictEqual(matched.expected, matched.observed);
  assert.equal(matched.expected.commit, fullCommit);
  assert.equal(matched.observed.commit, observed.commit);

  for (const expectedCommit of ['', 'abc', `${fullCommit}0`, 'g'.repeat(40)]) {
    assert.equal(evidence.evaluateRuntimeBuildIdentity({ expectedCommit, observed }).matched, false);
  }
  for (const commit of [undefined, '', 'abc', `${fullCommit}0`, 'g'.repeat(40)]) {
    assert.equal(evidence.evaluateRuntimeBuildIdentity({
      expectedCommit: fullCommit,
      observed: { ...observed, commit }
    }).matched, false);
  }
  assert.equal(evidence.evaluateRuntimeBuildIdentity({
    expectedCommit: fullCommit,
    observed: { ...observed, commit: 'fedcba9876543210fedcba9876543210fedcba98' }
  }).matched, false);
  assert.equal(evidence.evaluateRuntimeBuildIdentity({
    expectedCommit: fullCommit,
    observed: { ...observed, dirty: 'true' }
  }).matched, false);
  assert.equal(evidence.evaluateRuntimeBuildIdentity({
    expectedCommit: fullCommit,
    observed: { ...observed, packageId: 'com.tachiguro.knownfirst.debug' }
  }).matched, false);
  assert.equal(evidence.evaluateRuntimeBuildIdentity({
    expectedCommit: fullCommit,
    observed: { ...observed, configuration: 'Release' }
  }).matched, false);

  const mismatched = evidence.evaluateRuntimeBuildIdentity({
    expectedCommit: fullCommit,
    observed: { ...observed, dirty: 'true' }
  });
  assert.throws(() => evidence.createSummary(summaryInput(mismatched)), /verified runtime build identity/i);
  assert.throws(() => evidence.createSummary(summaryInput({ ...mismatched, matched: true })),
    /verified runtime build identity/i);
  assert.doesNotThrow(() => evidence.createSummary(summaryInput(matched)));
});

test('P16-A run directory accepts only a strict canonical descendant of the repository runs root', async (t) => {
  const evidence = await import(pathToFileURL(evidenceModulePath));
  assert.equal(typeof evidence.resolveAndroidRunDirectory, 'function');

  mkdirSync(runsRoot, { recursive: true });
  const validChild = mkdtempSync(join(runsRoot, 'boundary-test-'));
  const sibling = mkdtempSync(join(dirname(runsRoot), 'boundary-sibling-'));
  const external = mkdtempSync(join(tmpdir(), 'knownfirst-boundary-'));
  const link = join(runsRoot, `boundary-link-${process.pid}-${Date.now()}`);

  try {
    assert.equal(evidence.resolveAndroidRunDirectory(validChild), validChild);
    assert.throws(() => evidence.resolveAndroidRunDirectory(runsRoot), /strict descendant/i);
    const traversal = join(validChild, '..', '..', basename(sibling));
    assert.throws(() => evidence.resolveAndroidRunDirectory(traversal), /outside|descendant/i);
    assert.throws(() => evidence.resolveAndroidRunDirectory(sibling), /outside|descendant/i);
    assert.throws(() => evidence.resolveAndroidRunDirectory(external), /outside|descendant/i);

    try {
      symlinkSync(external, link, process.platform === 'win32' ? 'junction' : 'dir');
      assert.throws(() => evidence.resolveAndroidRunDirectory(link), /outside|descendant/i);
    } catch (error) {
      if (existsSync(link)) throw error;
      t.diagnostic(`Symlink escape assertion skipped because link creation is unavailable: ${error.code ?? error.message}`);
    }
  } finally {
    rmSync(link, { force: true, recursive: true });
    rmSync(validChild, { force: true, recursive: true });
    rmSync(sibling, { force: true, recursive: true });
    rmSync(external, { force: true, recursive: true });
  }
});
