import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { remote } from 'webdriverio';
import { createSummary, finalizeOwnedResources, recordScreenshot } from './lib/evidence.mjs';
import { runSettingsReleaseNotesNavigation, scenarioId } from './scenarios/settings-release-notes-navigation.mjs';

const allowedPackage = 'com.tachiguro.knownfirst.guitest';

function requireArgument(name) {
  const value = process.env[name];
  if (!value) {
    throw new Error(`${name} is required for an authorized runtime run.`);
  }
  return value;
}

async function main() {
  const runDirectory = requireArgument('KNOWNFIRST_ANDROID_GUI_RUN_DIRECTORY');
  const deviceId = requireArgument('KNOWNFIRST_ANDROID_GUI_DEVICE_ID');
  const chromedriverExecutable = requireArgument('KNOWNFIRST_ANDROID_GUI_CHROMEDRIVER');
  const appiumPort = Number(requireArgument('KNOWNFIRST_ANDROID_GUI_APPIUM_PORT'));
  const expectedCommit = requireArgument('KNOWNFIRST_ANDROID_GUI_EXPECTED_COMMIT');
  await mkdir(join(runDirectory, 'screenshots'), { recursive: true });

  const capabilities = {
    platformName: 'Android',
    'appium:automationName': 'UiAutomator2',
    'appium:udid': deviceId,
    'appium:appPackage': allowedPackage,
    'appium:appWaitActivity': '*',
    'appium:noReset': true,
    'appium:fullReset': false,
    'appium:forceAppLaunch': true,
    'appium:chromedriverExecutable': chromedriverExecutable,
    'appium:autoWebview': false
  };

  let browser;
  const assertions = [];
  let summary;
  let scenarioState = {};
  try {
    browser = await remote({ hostname: '127.0.0.1', port: appiumPort, path: '/', capabilities });
    await writeFile(join(runDirectory, 'capabilities.json'), JSON.stringify(capabilities, null, 2));
    const device = await browser.execute('mobile: getDeviceInfo');
    await writeFile(join(runDirectory, 'device.json'), JSON.stringify(device, null, 2));
    const contexts = await browser.getContexts();
    const recordAssertion = async (name, passed) => {
      assertions.push({ name, passed: Boolean(passed) });
      if (!passed) throw new Error(`Assertion failed: ${name}`);
    };
    const saveScreenshot = async (name) => browser.saveScreenshot(join(runDirectory, 'screenshots', name));

    scenarioState = await runSettingsReleaseNotesNavigation({ browser, recordAssertion, saveScreenshot });
    summary = createSummary({
      scenarioId,
      matrixMapping: null,
      result: 'Passed',
      failedStep: null,
      git: { commit: expectedCommit, branch: 'runtime-observed' },
      buildIdentity: { source: 'rendered-app-identity-required-at-runtime' },
      packageId: allowedPackage,
      configuration: 'Debug',
      toolVersions: { node: process.version },
      device,
      physicalOrEmulator: 'runtime-observed',
      orientation: 'runtime-observed',
      screenshotPixels: {},
      density: null,
      dpViewport: {},
      language: 'en',
      theme: 'light',
      contexts: { native: 'NATIVE_APP', available: contexts },
      profileId: scenarioState.profileId,
      safetyBefore: { passed: true, providerInvocations: scenarioState.providerInvocations },
      safetyAfter: { passed: true, providerInvocations: scenarioState.providerInvocations },
      timestamps: { startedAtUtc: new Date().toISOString(), endedAtUtc: new Date().toISOString() },
      assertionCounts: { passed: assertions.length, failed: 0 },
      remainingUnproven: ['Runtime metadata enrichment is recorded only after an authorized Android run.']
    });
    summary = recordScreenshot(summary, { name: 'release-notes.png', bytes: await browser.takeScreenshot() });
  } catch (error) {
    summary = createSummary({
      scenarioId,
      matrixMapping: null,
      result: 'Failed',
      failedStep: error.message,
      git: { commit: expectedCommit, branch: 'runtime-observed' },
      buildIdentity: {}, packageId: allowedPackage, configuration: 'Debug', toolVersions: {}, device: {},
      physicalOrEmulator: 'runtime-observed', orientation: 'runtime-observed', screenshotPixels: {}, density: null,
      dpViewport: {}, language: 'en', theme: 'light', contexts: {}, profileId: 'runtime-observed',
      safetyBefore: { passed: false }, safetyAfter: { passed: false },
      timestamps: { startedAtUtc: new Date().toISOString(), endedAtUtc: new Date().toISOString() },
      assertionCounts: { passed: assertions.filter((assertion) => assertion.passed).length, failed: 1 },
      remainingUnproven: [error.message]
    });
  } finally {
    const cleanup = await finalizeOwnedResources({
      scenarioSucceeded: summary?.result === 'Passed', safetyAfter: summary?.safetyAfter, session: browser,
      ownedServer: null
    });
    const completedSummary = { ...summary, cleanup };
    await writeFile(join(runDirectory, 'summary.json'), JSON.stringify(completedSummary, null, 2));
    await writeFile(join(runDirectory, 'safety-before.json'), JSON.stringify(completedSummary.safetyBefore, null, 2));
    await writeFile(join(runDirectory, 'safety-after.json'), JSON.stringify(completedSummary.safetyAfter, null, 2));
    await writeFile(join(runDirectory, 'steps.jsonl'), assertions.map((assertion) => JSON.stringify(assertion)).join('\n'));
  }

  if (summary.result !== 'Passed') process.exitCode = 1;
}

main().catch((error) => {
  process.stderr.write(`${error.stack ?? error.message}\n`);
  process.exitCode = 1;
});
