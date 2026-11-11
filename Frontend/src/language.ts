export type Language = 'en' | 'bn'
export const LANGUAGE_STORAGE_KEY = 'school-admission.language'
import { bangla } from './translations.ts'

export function readLanguage(storage?: Pick<Storage, 'getItem'> | null): Language {
  try { return storage?.getItem(LANGUAGE_STORAGE_KEY) === 'bn' ? 'bn' : 'en' } catch { return 'en' }
}
export function saveLanguage(language: Language, storage?: Pick<Storage, 'setItem'> | null) {
  try { storage?.setItem(LANGUAGE_STORAGE_KEY, language) } catch { /* Language selection still works without storage. */ }
}
export function translate(language: Language, text: string, values: Record<string, string | number> = {}) {
  return (language === 'bn' ? bangla[text] ?? text : text).replace(/\{(\w+)\}/g, (token, name: string) => String(values[name] ?? token))
}
export function localeFor(language: Language) { return language === 'bn' ? 'bn-BD' : 'en-GB' }
