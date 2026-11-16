import type { Translation } from '../types'
import type de from '../de/cabinet'

// Namespace "cabinet": English texts, typed against the German source file.
const cabinet = {
  title: 'Trophy cabinet',
  subtitle: 'All your games at 100%',
  stats: {
    games_one: '{{count, number}} platinum game',
    games_other: '{{count, number}} platinum games',
    gamesHint: 'The number of games you have taken to 100%.',
    trophies_one: '{{count, number}} trophy',
    trophies_other: '{{count, number}} trophies',
    trophiesHint: 'All trophies of these games combined.',
    trophiesTotal_one: '{{count, number}} trophy in total',
    trophiesTotal_other: '{{count, number}} trophies in total',
    playtime: '{{time}} playtime',
    playtimeHint: 'Total playtime in these games.',
    playtimeTotal: '{{time}} total playtime',
  },
  figure: {
    games_one: 'platinum game',
    games_other: 'platinum games',
    trophies_one: 'trophy',
    trophies_other: 'trophies',
    playtime: 'Playtime',
  },
  hours: '{{value}} h',
  size: {
    label: 'Size in the cabinet',
    description: 'Left: smaller, more games per shelf – right: larger.',
    step: {
      veryS: 'Very small',
      small: 'Small',
      medium: 'Medium',
      large: 'Large',
      veryL: 'Very large',
    },
  },
  view: {
    lighting: 'Lighting',
    spots: { name: 'Spots', hint: 'One lamp above every game' },
    led: { name: 'LED', hint: 'One continuous strip per shelf' },
    showEmpty: { name: 'Show empty places', hint: 'Thinly outlined placeholders for the places that are still free' },
  },
  doors: {
    close: { label: 'Close doors', description: 'The light goes out and the glass doors close.' },
    open: { label: 'Open doors', description: 'The glass doors open, then the light comes on.' },
    closedState: 'Doors closed',
  },
  plaque: {
    time: '{{time}}',
    stamp: 'Platinum on {{date}} at {{time}}',
  },
  info: {
    panel: 'Details for {{name}}',
    close: 'Close details',
    platinumOn: 'Platinum on {{date}}',
    trophies: 'Trophies',
    unlocked: '{{n}} unlocked',
    duration: 'Duration',
    sameDay: '100% on the same day',
    days_one: '{{count}} day to 100%',
    days_other: '{{count, number}} days to 100%',
    playtime: 'Playtime',
    averageRarity: 'Average rarity per trophy',
    ofPlayers: '{{percent}} of players',
    rarest: 'Rarest trophy',
    toGame: 'Go to game page',
  },
  loading: 'Stocking the cabinet …',
  error: {
    title: 'The trophy cabinet could not be loaded',
    unknown: 'Unknown error',
  },
  scroll: {
    prev: 'Scroll left',
    next: 'Scroll right',
  },
  presented: {
    badge: 'New',
    badgeLabel: 'New in the cabinet',
    announce_one: 'New in the cabinet: {{names}}',
    announce_other: 'New in the cabinet: {{count}} games, including {{names}}',
  },
  empty: {
    title: 'The cabinet is still empty',
    text: 'As soon as you have all trophies in a game, it gets its place here – with cover, nameplate and all key figures.',
  },
} satisfies Translation<typeof de>

export default cabinet
