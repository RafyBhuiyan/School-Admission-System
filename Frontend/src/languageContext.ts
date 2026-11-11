import { createContext, useContext } from 'react'
import type { Language } from './language.ts'
export const LanguageContext = createContext<{
  language: Language; setLanguage: (value: Language) => void
  t: (text: string, values?: Record<string, string | number>) => string
  date: (value: string) => string; number: (value: number) => string
} | null>(null)
export function useLanguage() {
  const value = useContext(LanguageContext)
  if (!value) throw new Error('LanguageProvider is required.')
  return value
}
