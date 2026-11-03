// Twitch link for a game without any key or account: twitch.tv/directory/game/<name> redirects to the game's category
// (live channels, signed out). Tested 2026-10-31 with 36 real Steam names: 33 direct hits; "NieR:Automata" only
// matched with a space after the colon, edition names ("… Complete Edition") did not match at all - those go to
// Twitch's search instead, which always lists the category.
const EDITION_NAME = /\b(edition|remastered|remaster|collection|goty|game of the year|director'?s cut|definitive|deluxe|ultimate|complete|enhanced|anniversary)\b/i

export function twitchGameName(name: string): string {
  return name
    .replace(/[™®©]/g, '')
    .replace(/\s*:\s*/g, ': ')
    .replace(/\s+/g, ' ')
    .trim()
}

export function twitchGameUrl(name: string): string {
  const clean = twitchGameName(name)
  return EDITION_NAME.test(clean)
    ? `https://www.twitch.tv/search?term=${encodeURIComponent(clean)}`
    : `https://www.twitch.tv/directory/game/${encodeURIComponent(clean)}`
}
