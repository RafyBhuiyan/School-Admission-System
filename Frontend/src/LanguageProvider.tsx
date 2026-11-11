import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { LanguageContext, useLanguage } from './languageContext.ts'
import { localeFor, readLanguage, saveLanguage, translate } from './language.ts'
import type { Language } from './language.ts'
export function LanguageProvider({ children }: { children: ReactNode }) {
  const [language, setLanguage] = useState<Language>(() => {
    try { return readLanguage(window.localStorage) } catch { return 'en' }
  })
  useEffect(() => {
    document.documentElement.lang = language
    document.title = translate(language, 'School admissions')
    try { saveLanguage(language, window.localStorage) } catch { /* Storage can be blocked. */ }
  }, [language])
  const locale = localeFor(language)
  return <LanguageContext.Provider value={{ language, setLanguage,
    t: (text, values) => translate(language, text, values),
    date: value => new Date(value).toLocaleString(locale),
    number: value => value.toLocaleString(locale),
  }}>{children}</LanguageContext.Provider>
}
export function LanguageSwitcher() {
  const { language, setLanguage } = useLanguage()
  const next = language === 'en' ? 'bn' : 'en'
  return <div className="language-switch">
    <button type="button" lang={next} aria-label={language === 'en' ? 'Switch to বাংলা' : 'Switch to English'} onClick={() => setLanguage(next)}>{language === 'en' ? 'বাংলা' : 'English'}</button>
  </div>
}
