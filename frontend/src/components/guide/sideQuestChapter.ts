import type { GuideTabDto } from '../../api/types'

// Fallback for chapters that do not carry their topic: the title or slug names the quests ("Nebenquests & Aktivitäten", "Side quests & activities", "Nebenaufgaben")
const QUEST_WORDS = /neben\s*-?\s*(quest|aufgabe|auftr)|side\s*-?\s*(quest|mission|activit|job)|nebenquests?|sidequests?|^quests?$/i

/**
 * The side quest chapter of a guide: the chapter researched for the standard topic "quests" (when the chapter says so), else the one whose title or slug names
 * side quests. `exceptId` leaves out the chapter that is being read (a chapter does not link to itself). null = the guide has none.
 */
export function findSideQuestChapter(tabs: GuideTabDto[], exceptId?: string): GuideTabDto | null {
  const candidates = tabs.filter((tab) => tab.id !== exceptId && !tab.isAchievementList)
  const byTopic = candidates.find((tab) => tab.topicId === 'quests')
  if (byTopic) return byTopic
  return candidates.find((tab) => QUEST_WORDS.test(tab.title) || QUEST_WORDS.test(tab.slug.replace(/^tab-\d+-/, '').replace(/-/g, ' '))) ?? null
}
