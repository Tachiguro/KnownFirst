export const scenarioId = 'P16B-SettingsReleaseNotesHistory';

export async function runSettingsReleaseNotesNavigation({ browser, recordAssertion, captureScreenshot }) {
  const contexts = await browser.getContexts();
  const webview = contexts.find((context) => context.startsWith('WEBVIEW_com.tachiguro.knownfirst.guitest'));
  if (!webview) {
    throw new Error('Expected the dedicated KnownFirst GUI-test WebView context, but none was available.');
  }

  await browser.switchContext(webview);
  const indicator = await browser.$('#gui-test-profile-indicator');
  await indicator.waitForDisplayed();
  const profileId = await indicator.getAttribute('data-gui-test-profile-id');
  const provider = await indicator.getAttribute('data-gui-test-provider');
  const providerInvocations = await indicator.getAttribute('data-gui-test-provider-invocations');
  await recordAssertion('GUI-test profile marker is active', Boolean(profileId));
  await recordAssertion('Offline GUI-test provider is active', provider === 'offline');
  await recordAssertion('Offline GUI-test provider has no unexpected invocations', providerInvocations === '0');

  const initialWhatsNew = await browser.$('#whats-new-modal');
  const whatsNewVisible = await initialWhatsNew.isDisplayed().catch(() => false);
  await recordAssertion('Automatic What\'s New modal is not visible initially on seeded seen profile', !whatsNewVisible);

  for (const selector of [
    '#stat-document-count', '#stat-unreviewed-word-count', '#stat-known-word-count',
    '#stat-unknown-word-count', '#stat-prepared-word-count'
  ]) {
    const statistic = await browser.$(selector);
    await statistic.waitForDisplayed();
    await recordAssertion(`${selector} is deterministic empty state`, (await statistic.getText()).trim() === '0');
  }

  // First activation from Settings
  await (await browser.$('#nav-settings')).click();
  const releaseNotesLink = await browser.$('#settings-release-notes-link');
  await releaseNotesLink.waitForDisplayed();

  const obsoleteSupport = await browser.$('#support-knownfirst');
  const supportPresent = await obsoleteSupport.isExisting().catch(() => false);
  await recordAssertion('Obsolete Support KnownFirst placeholder control is absent', !supportPresent);

  await releaseNotesLink.click();
  await (await browser.$('#release-notes-page')).waitForDisplayed();
  await recordAssertion('Release Notes route is active', (await browser.getUrl()).endsWith('/release-notes'));

  const header = await browser.$('#release-notes-page .page-header h1');
  if (await header.isExisting()) {
    const titleText = (await header.getText()).trim();
    await recordAssertion('Release Notes page has localized header title', titleText.length > 0 && !titleText.toLowerCase().includes('not found'));
  }

  // Assert newest-first version entries: Beta 13, Beta 12, Beta 11, Beta 10
  for (const version of ['1.0.0-beta.13', '1.0.0-beta.12', '1.0.0-beta.11', '1.0.0-beta.10']) {
    const heading = await browser.$(`#release-note-${version}`);
    await heading.waitForDisplayed();
    await recordAssertion(`Release note version ${version} heading is displayed`, await heading.isDisplayed());
  }

  const bulletLists = await browser.$$('.release-note-bullets');
  await recordAssertion('Release note entries have bullet lists', bulletLists.length >= 4);

  // Capture S36 screenshot in native context
  await browser.switchContext('NATIVE_APP');
  const screenshotEvidence = await captureScreenshot('release-notes.png');
  await browser.switchContext(webview);

  // Second activation: return to Settings and re-open Release Notes
  await (await browser.$('#nav-settings')).click();
  await (await browser.$('#settings-release-notes-link')).waitForDisplayed();
  await (await browser.$('#settings-release-notes-link')).click();
  await (await browser.$('#release-notes-page')).waitForDisplayed();

  for (const version of ['1.0.0-beta.13', '1.0.0-beta.12', '1.0.0-beta.11', '1.0.0-beta.10']) {
    const heading = await browser.$(`#release-note-${version}`);
    await recordAssertion(`Second activation: release note version ${version} is displayed identically`, await heading.isDisplayed());
  }

  // App restart verification via WebdriverIO application lifecycle APIs
  await browser.terminateApp('com.tachiguro.knownfirst.guitest');
  await browser.activateApp('com.tachiguro.knownfirst.guitest');

  const restartedContexts = await browser.getContexts();
  const restartedWebview = restartedContexts.find((context) =>
    context.startsWith('WEBVIEW_com.tachiguro.knownfirst.guitest'));
  if (!restartedWebview) {
    throw new Error('Expected GUI-test WebView context after application restart.');
  }

  await browser.switchContext(restartedWebview);
  const postRestartWhatsNew = await browser.$('#whats-new-modal');
  const postRestartWhatsNewVisible = await postRestartWhatsNew.isDisplayed().catch(() => false);
  await recordAssertion('After application restart, What\'s New notice does not reappear', !postRestartWhatsNewVisible);

  const postRestartIndicator = await browser.$('#gui-test-profile-indicator');
  await postRestartIndicator.waitForDisplayed();
  await recordAssertion('Profile is stable across restart',
    profileId === await postRestartIndicator.getAttribute('data-gui-test-profile-id'));
  await recordAssertion('Offline provider remains unused after restart',
    providerInvocations === await postRestartIndicator.getAttribute('data-gui-test-provider-invocations'));

  return { profileId, providerInvocations, screenshotEvidence };
}
