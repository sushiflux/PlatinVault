import type { Translation } from '../types'
import type de from '../de/shell'

// Namespace "shell": English texts, typed against the German source file.
const shell = {
  header: {
    utilsToolbar: 'Language, colour scheme, settings and help',
    theme: {
      label: 'Colour scheme: {{current}}',
      description: 'Click to switch to “{{next}}”',
      light: 'Light',
      dark: 'Dark',
    },
    settings: {
      label: 'Settings',
      description: 'Popups, Steam, AI keys, bridge and features',
    },
    help: {
      label: 'Help & hints',
      description: 'Show the hints for this page again',
    },
    // The "update available" icon and its popover (components/updates/UpdateButton.tsx)
    update: {
      label: 'Update available',
      labelVersion: 'Update available: {{version}}',
      description: '{{version}} – click for details',
      panel: 'Update available',
      titleRelease: 'New release available',
      titleBeta: 'New beta available',
      youHave: 'installed: {{current}}',
      noInstaller: 'There is no installer for this version yet. See Settings → System → Updates for more.',
      allSettings: 'All update settings',
      container: {
        intro: 'Your server updates the container, not PlatinVault itself. Your data is kept.',
        unraid: 'Docker tab → “Check for Updates” → “Update”.',
        otherTitle: 'Other NAS',
        other: 'Recreate the container in your NAS container manager with the new image.',
      },
    },
    handbook: {
      label: 'Handbook',
      description: 'Open the handbook as a PDF right here in the app',
    },
    language: {
      label: 'Language',
      labelCurrent: 'Language: {{current}}',
      description: 'Applies to the whole app and is saved.',
      menu: 'Choose language',
      saveFailed: 'The language could not be saved',
    },
    scale: {
      label: 'Display size',
      labelCurrent: 'Display size, currently {{percent}} percent',
      descriptionDefault: 'Make text, spacing and controls larger or smaller – the page width stays',
      descriptionCurrent: 'Currently {{percent}} – click to change',
      announce: 'Display size {{percent}} percent',
      smaller: 'Smaller',
      larger: 'Larger',
      pickStep: 'Choose a step',
      hint: 'Text, spacing and controls – the page width stays the same.',
      isDefault: 'Default size ({{percent}}) is set',
      resetTo: 'Reset to {{percent}}',
    },
  },
  footer: {
    copyright: '© {{year}} {{company}}',
    licenses: 'Licences',
    licensesHint: 'Licence (GNU AGPL v3), source code and notices for the open-source components included',
    infoLabel: 'Info on version and licences',
    about: 'About {{name}}',
    // The version as a button with its popover (components/updates/VersionCheck.tsx)
    version: {
      title: 'Version & updates',
      hint: 'Check for updates and see the last result',
      check: 'Check for updates',
      auto: 'Automatic check: {{interval}}',
      seeIcon: 'The update icon at the top right shows the details.',
      settingsHint: 'How often PlatinVault looks by itself is set under Settings → System → Updates.',
    },
  },
  madeWith: {
    text: 'Made with <heart/>',
    heart: 'heart',
  },
  scrollTop: 'Back to top',
  handbook: {
    title: 'Handbook',
    languageGroup: 'Language of the handbook',
    download: 'Download',
    downloadLabel: 'Download the handbook',
    openTab: 'Open in new tab',
    openTabLabel: 'Open the handbook in a new tab',
    close: 'Close the handbook',
    frameTitle: 'Handbook ({{language}})',
    noPdfViewer: 'This browser cannot show PDF files directly. Open the handbook in a new tab or download it.',
  },
  licenses: {
    title: 'Licences',
    docGroup: 'Licence document',
    license: {
      label: 'Licence (AGPL v3)',
      hint: 'PlatinVault is free software under the GNU Affero General Public License, version 3 (AGPL-3.0-only). You may use, study, change and share it free of charge; whoever passes it on or offers a changed version to others over a network must offer the source code under the same licence. The original text below is binding.',
    },
    notices: {
      label: 'Third-party notices',
      hint: 'Copyright notices and licence texts of the open-source components included.',
    },
    source: 'Source code',
    sourceHint: 'The complete source code of PlatinVault: {{url}}',
    sourceLabel: 'Open the source code of PlatinVault on GitHub',
    sourceLine: 'Source code:',
    download: 'Download',
    downloadHint: 'Save {{fileName}} as a text file',
    downloadLabel: 'Download {{document}}',
    openTab: 'Open in new tab',
    openTabHint: 'Show as plain text in a browser tab',
    openTabLabel: 'Open {{document}} in a new tab',
    close: 'Close',
    loading: 'Loading {{document}} …',
    loadFailed: '{{document}} could not be loaded: {{reason}}',
    retry: 'Try again',
  },
  steamChip: {
    fallbackLabel: 'Steam account',
    openInClient: 'Open Steam profile in the Steam client',
    openInBrowser: 'Open Steam profile in the browser',
    libraryDescription: '{{games}} in your Steam library',
    games_one: '{{count, number}} game',
    games_other: '{{count, number}} games',
    linkClient: 'Open the Steam profile of {{name}} in the Steam client',
    linkBrowser: 'Open the Steam profile of {{name}} in the browser',
    linkLibrary: '{{games}} in the library',
  },
} satisfies Translation<typeof de>

export default shell
